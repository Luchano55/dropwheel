using System.Windows.Interop;
using System.Windows.Threading;
using DropWheel.Conversions;
using DropWheel.Documents;
using DropWheel.Engines;
using Microsoft.Web.WebView2.Core;

namespace DropWheel.AppEngines;

/// <summary>
/// HTML → PDF встроенным в Windows браузером Edge (WebView2 Runtime, CoreWebView2.PrintToPdfAsync):
/// «Текст → PDF» (TXT, Markdown, HTML) и запасной путь «Word → PDF» без Office — только DOCX
/// (через DocxHtml: «почти как в Word», см. D-DOCLIBS).
/// Edge живёт в невидимом окне на потоке интерфейса (WebView2 требует STA с циклом сообщений):
/// один на всё приложение, создаётся при первой печати или заранее (Prepare), закрывается после 2 минут простоя.
/// Поток интерфейса никогда не блокируется: вся работа с WebView2 — асинхронная, через Dispatcher.
/// </summary>
public sealed class WebPdfEngine : IInternalEngine, IDisposable
{
    public const string TextToPdfId = "text-to-pdf";
    public const string WordToPdfId = "word-to-pdf";

    /// <summary>Предел на одну печать: запуск Edge + загрузка страницы + PDF.</summary>
    public static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(60);
    /// <summary>Страница грузится дольше (ждёт сеть) — печатаем то, что успело загрузиться.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleClose = TimeSpan.FromMinutes(2);

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int ViewWidth = 1024, ViewHeight = 768;

    private readonly Dispatcher _dispatcher;
    private readonly string _userDataFolder;
    private readonly Action<string>? _log;
    private readonly Lazy<string?> _runtimeVersion = new(FindRuntime);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    // Только в потоке интерфейса.
    private Task<CoreWebView2Environment>? _environment;
    private Task<Host>? _host;
    private DispatcherTimer? _idleTimer;
    private bool _busy;

    private sealed record Host(HwndSource Window, CoreWebView2Controller Controller);

    /// <param name="dispatcher">Диспетчер потока интерфейса WPF.</param>
    /// <param name="userDataFolder">Профиль Edge, по умолчанию %LocalAppData%\DropWheel\WebView2.</param>
    public WebPdfEngine(Dispatcher dispatcher, string userDataFolder, Action<string>? log = null)
    {
        _dispatcher = dispatcher;
        _userDataFolder = userDataFolder;
        _log = log;
    }

    public EngineKind Kind => EngineKind.WebPdf;

    /// <summary>Установлен ли WebView2 Runtime (в Windows 11 и обновлённой Windows 10 — всегда).</summary>
    public bool IsAvailable => _runtimeVersion.Value is not null;

    public string? RuntimeVersion => _runtimeVersion.Value;

    public bool CanRun(Operation op) => op.Id is WordToPdfId or TextToPdfId && IsAvailable;

    /// <summary>Word → PDF — только DOCX/DOCM (DOC, RTF, ODT без Office не прочитать); текст — TXT, MD, HTML.</summary>
    public bool CanRun(Operation op, string extension) =>
        CanRun(op) && (op.Id == WordToPdfId ? DocLibsEngine.WordInputs : Extensions.Text).Contains(extension);

    private static string? FindRuntime()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (Exception)
        {
            // WebView2RuntimeNotFoundException, нет WebView2Loader.dll и т. п. — значит, недоступен.
            return null;
        }
    }

    /// <summary>Пользователь видит меню с печатью в PDF — запустить Edge заранее, не дожидаясь.</summary>
    public void Prepare(IReadOnlyList<Operation> offered)
    {
        if (_disposed || !offered.Any(CanRun)) return;
        _dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await EnsureHostAsync();
                RestartIdleTimer();
            }
            catch (Exception ex)
            {
                _log?.Invoke("WebView2: не удалось запустить заранее: " + ex.Message);
                ResetHost();
            }
        });
    }

    public async Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var input = inputs[0];
        var extension = Extensions.Normalize(Path.GetExtension(input));
        if (!CanRun(op, extension))
            throw new ConversionException(IsAvailable
                ? $"Edge не печатает в PDF файлы .{extension}"
                : "Не найден Microsoft Edge WebView2 Runtime", op.Id);

        // HTML готовим в фоне: разбор DOCX может занять секунды.
        var source = await Task.Run(() => PrepareSource(op, input, extension, ct), ct);
        var pdf = Path.Combine(TempDir, Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await _gate.WaitAsync(ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(JobTimeout);
                try
                {
                    await _dispatcher.InvokeAsync(() => PrintAsync(source, pdf, progress, timeout.Token)).Task.Unwrap();
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new ConversionException($"Edge не успел напечатать PDF за {JobTimeout.TotalSeconds:0} секунд", input);
                }
            }
            finally
            {
                _gate.Release();
            }
            // Печать идёт во временную папку: если Edge «зависнет» и допишет файл позже, мусор не останется у пользователя.
            File.Move(pdf, output, overwrite: true);
        }
        finally
        {
            TryDelete(pdf);
            if (source.TempFile is not null) TryDelete(source.TempFile);
        }
    }

    // ---------- Что печатаем ----------

    private sealed record Source(string Uri, PageSetup Page, bool Scripts, string? TempFile);

    private static string TempDir
    {
        get
        {
            var dir = Path.Combine(Path.GetTempPath(), "DropWheel", "pdf");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static Source PrepareSource(Operation op, string input, string extension, CancellationToken ct)
    {
        var title = Path.GetFileNameWithoutExtension(input);
        if (op.Id == WordToPdfId)
        {
            var docx = DocxHtml.Convert(input, ct);
            return TempPage(docx.Html, docx.Page);
        }
        return extension switch
        {
            // Сам файл: относительные картинки и стили грузятся из его папки, скрипты страницы работают.
            "html" or "htm" => new Source(HtmlPages.FileUri(input), HtmlPages.TextPage, Scripts: true, TempFile: null),
            "md" or "markdown" => TempPage(HtmlPages.FromMarkdown(TextFiles.Read(input), title,
                HtmlPages.FileUri(Path.GetDirectoryName(Path.GetFullPath(input))!, addTrailingSlash: true)), HtmlPages.TextPage),
            _ => TempPage(HtmlPages.FromPlainText(TextFiles.Read(input), title), HtmlPages.TextPage),
        };
    }

    private static Source TempPage(string html, PageSetup page)
    {
        var file = Path.Combine(TempDir, Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(file, html, TextFiles.Utf8);
        return new Source(HtmlPages.FileUri(file), page, Scripts: false, file);
    }

    // ---------- Печать (поток интерфейса) ----------

    private async Task PrintAsync(Source source, string pdf, IProgress<double>? progress, CancellationToken ct)
    {
        _busy = true;
        _idleTimer?.Stop();
        var reusable = false;
        try
        {
            var host = await EnsureHostAsync().WaitAsync(ct);
            var web = host.Controller.CoreWebView2;
            web.Settings.IsScriptEnabled = source.Scripts;

            var loaded = await NavigateAsync(web, source.Uri, ct);
            progress?.Report(0.5);

            var environment = await _environment!;
            var print = environment.CreatePrintSettings();
            print.Orientation = CoreWebView2PrintOrientation.Portrait; // ширина и высота листа уже с учётом ориентации
            print.PageWidth = PageSetup.Inches(source.Page.WidthMm);
            print.PageHeight = PageSetup.Inches(source.Page.HeightMm);
            print.MarginTop = PageSetup.Inches(source.Page.TopMm);
            print.MarginRight = PageSetup.Inches(source.Page.RightMm);
            print.MarginBottom = PageSetup.Inches(source.Page.BottomMm);
            print.MarginLeft = PageSetup.Inches(source.Page.LeftMm);
            print.ScaleFactor = 1.0;
            print.ShouldPrintBackgrounds = true;
            print.ShouldPrintHeaderAndFooter = false; // без адреса файла и даты на полях

            if (!await web.PrintToPdfAsync(pdf, print).WaitAsync(ct))
                throw new ConversionException("Edge не смог сохранить PDF", source.Uri);
            // Недогруженную страницу остановили — её события ещё могут прийти; такой Edge не переиспользуем.
            reusable = loaded;
        }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            throw new ConversionException("Edge (WebView2) не смог напечатать PDF", ex.Message, ex);
        }
        finally
        {
            _busy = false;
            if (reusable) RestartIdleTimer();
            else ResetHost();
        }
    }

    /// <returns>false — страница не догрузилась за LoadTimeout и остановлена.</returns>
    private static async Task<bool> NavigateAsync(CoreWebView2 web, string uri, CancellationToken ct)
    {
        var done = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e);

        web.NavigationCompleted += OnCompleted;
        using var delay = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            web.Navigate(uri);
            var finished = await Task.WhenAny(done.Task, Task.Delay(LoadTimeout, delay.Token));
            ct.ThrowIfCancellationRequested();
            if (finished != done.Task)
            {
                web.Stop();
                return false;
            }
            var result = await done.Task;
            if (!result.IsSuccess && result.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                throw new ConversionException("Edge не смог открыть файл", $"{result.WebErrorStatus}: {uri}");
            return true;
        }
        finally
        {
            delay.Cancel();
            web.NavigationCompleted -= OnCompleted;
        }
    }

    // ---------- Edge в невидимом окне (поток интерфейса) ----------

    private Task<Host> EnsureHostAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _host ??= CreateHostAsync();
    }

    private async Task<Host> CreateHostAsync()
    {
        _environment ??= CreateEnvironmentAsync();
        CoreWebView2Environment environment;
        try
        {
            environment = await _environment;
        }
        catch
        {
            _environment = null; // в следующий раз попробуем снова
            throw;
        }

        // Окно без WS_VISIBLE: никогда не показывается, фокус не забирает.
        var window = new HwndSource(new HwndSourceParameters("DropWheel PDF")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            PositionX = -32000,
            PositionY = -32000,
            Width = ViewWidth,
            Height = ViewHeight,
        });
        try
        {
            var controller = await environment.CreateCoreWebView2ControllerAsync(window.Handle);
            controller.Bounds = new System.Drawing.Rectangle(0, 0, ViewWidth, ViewHeight);
            controller.IsVisible = true; // «видимый» внутри невидимого окна — без фонового торможения страницы

            var web = controller.CoreWebView2;
            var s = web.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDefaultScriptDialogsEnabled = false; // alert() на странице не повесит печать
            s.AreDevToolsEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsWebMessageEnabled = false;
            s.IsZoomControlEnabled = false;
            web.NewWindowRequested += (_, e) => e.Handled = true; // всплывающие окна страниц не открываем
            web.DownloadStarting += (_, e) => e.Cancel = true;
            web.ScriptDialogOpening += (_, _) => { };             // alert/confirm закрываются сразу («Отмена»)

            _log?.Invoke($"WebView2 {environment.BrowserVersionString}: Edge для печати в PDF запущен");
            return new Host(window, controller);
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    private Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        Directory.CreateDirectory(_userDataFolder);
        return CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _userDataFolder, options: null);
    }

    private void RestartIdleTimer()
    {
        if (_disposed) return;
        _idleTimer ??= new DispatcherTimer(IdleClose, DispatcherPriority.Background, (_, _) =>
        {
            _idleTimer?.Stop();
            if (!_busy) ResetHost();
        }, _dispatcher);
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    /// <summary>Закрыть Edge (после ошибки, по простою, при выходе). Если он ещё создаётся — закрыть, когда создастся.</summary>
    private void ResetHost()
    {
        _idleTimer?.Stop();
        var host = _host;
        _host = null;
        if (host is null) return;
        if (host.IsCompleted) CloseHost(host);
        else host.ContinueWith(CloseHost, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void CloseHost(Task<Host> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            _ = task.Exception; // ошибка уже обработана — не даём ей всплыть как «ненаблюдаемой»
            return;
        }
        try { task.Result.Controller.Close(); } catch { /* уже закрыт */ }
        try { task.Result.Window.Dispose(); } catch { /* уже закрыто */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_dispatcher.CheckAccess()) ResetHost();
        else if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(ResetHost);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* не критично */ }
    }
}
