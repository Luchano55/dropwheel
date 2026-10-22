using System.Diagnostics;
using System.Runtime.InteropServices;
using DropWheel.Conversions;
using DropWheel.Engines;
using Microsoft.Win32;
using DropWheel.Localization;

namespace DropWheel.AppEngines;

/// <summary>
/// Одно приложение Office в потоке Office (StaWorker из OfficeEngine): запуск невидимым, конвертация, выход.
/// Все методы, кроме Kill и флагов, вызываются только из этого потока.
/// <para>
/// Позднее связывание (dynamic, IDispatch) — без сборок взаимодействия: работает с любой версией Office.
/// Каждый объект COM, который мы получили, освобождаем сами (Release) — иначе Excel не выходит.
/// </para>
/// <para>
/// Всегда новый экземпляр (Activator.CreateInstance, никогда GetActiveObject): окна пользователя не трогаем.
/// Word и Excel на каждый CreateInstance запускают свой процесс. PowerPoint — однoэкземплярный: если он уже
/// открыт пользователем, COM отдаёт тот же процесс. Такой экземпляр «чужой» (не Owned): его не закрываем
/// и не убиваем, DisplayAlerts/AutomationSecurity меняем только на время задачи.
/// </para>
/// </summary>
internal abstract class OfficeHost(Action<string> log)
{
    /// <summary>msoAutomationSecurityForceDisable: макросы в открываемых нами файлах не запускаются.</summary>
    protected const int ForceDisableMacros = 3;
    /// <summary>Пароль-заглушка: для файла с паролем Office вернёт ошибку, а не покажет невидимое окно с вопросом.</summary>
    protected const string DummyPassword = "DropWheel-no-password";
    protected const int MsoTrue = -1, MsoFalse = 0;
    /// <summary>Сколько ждать выхода процесса после Quit, прежде чем завершить его принудительно.</summary>
    private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(3);

    protected readonly Action<string> Log = log;
    /// <summary>Объект Application; null — не запущен.</summary>
    protected dynamic? App;

    private int _baseline;                     // документов в нашем экземпляре сразу после запуска (обычно 0)
    private volatile int _pid;                 // процесс Office; 0 — неизвестен
    private volatile bool _owned;              // процесс запустили мы, а не подключились к открытому пользователем
    private volatile bool _dead;               // процесс убит или пропал — при следующей задаче запустить заново
    private volatile bool _running;
    private volatile HashSet<int>? _starting;  // процессы до CreateInstance, пока идёт запуск (для Kill)

    /// <summary>Задач этого приложения в очереди или в работе — не закрывать по простою.</summary>
    public int Pending;
    /// <summary>1 — прогрев уже стоит в очереди.</summary>
    public int PrewarmQueued;

    public DateTime LastUsedUtc { get; private set; }
    public bool IsRunning => _running;
    /// <summary>Процесс нашего экземпляра (0 — неизвестен или экземпляр чужой). Для тестов и лога.</summary>
    public int OwnedProcessId => _owned ? _pid : 0;
    protected bool IsOwned => _owned;

    public abstract OfficeApp Kind { get; }
    public string Name => Kind.ToString();
    protected abstract string ProcessName { get; }
    /// <summary>Значение DisplayAlerts «ничего не спрашивать» (у каждого приложения своё).</summary>
    protected abstract object QuietAlerts { get; }
    /// <summary>Коллекция открытых файлов: Documents / Workbooks / Presentations.</summary>
    protected abstract dynamic Documents();
    protected abstract bool IsVisible();
    protected abstract int? FindProcessId();
    protected virtual void ConfigureOwned() { }
    protected abstract void QuitApp();
    protected abstract void Convert(OfficeTarget target, string input, string output, IProgress<double>? progress, CancellationToken ct);

    public static string ProgIdOf(OfficeApp app) => app switch
    {
        OfficeApp.Word => "Word.Application",
        OfficeApp.Excel => "Excel.Application",
        _ => "PowerPoint.Application",
    };

    /// <summary>Установлено ли приложение: ProgID зарегистрирован (только чтение реестра, Office не запускается).</summary>
    public static bool IsInstalled(OfficeApp app)
    {
        try { return Type.GetTypeFromProgID(ProgIdOf(app), throwOnError: false) is not null; }
        catch { return false; }
    }

    // ---------- Задачи (поток Office) ----------

    /// <summary>Прогрев: запустить заранее, пока пользователь выбирает сектор колеса.</summary>
    public void Prewarm()
    {
        EnsureStarted();
        LastUsedUtc = DateTime.UtcNow;
    }

    public void Run(OfficeTarget target, string input, string output, IProgress<double>? progress, CancellationToken ct)
    {
        EnsureStarted();
        ct.ThrowIfCancellationRequested();
        progress?.Report(0.1);

        var shared = !_owned;
        object? alerts = null, security = null;
        try
        {
            if (shared)
            {
                // Чужой экземпляр (PowerPoint пользователя): «не спрашивать» и запрет макросов — только на время задачи.
                alerts = App!.DisplayAlerts;
                security = App.AutomationSecurity;
                App.DisplayAlerts = QuietAlerts;
                App.AutomationSecurity = ForceDisableMacros;
            }
            // Файл уже открыт в этом экземпляре (бывает только с чужим): Open вернул бы документ пользователя,
            // а мы бы его закрыли.
            if (IsOpen(input))
                throw new ConversionException(Loc.F(Loc.T.ErrFileOpenIn, Name), input);
            Convert(target, input, output, progress, ct);
        }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            throw Translate(ex, Loc.F(Loc.T.ErrAppCantConvert, Name));
        }
        finally
        {
            LastUsedUtc = DateTime.UtcNow;
            if (shared && alerts is not null)
            {
                try
                {
                    App!.DisplayAlerts = alerts;
                    App.AutomationSecurity = security;
                }
                catch (Exception ex) { Log($"{Name}: не удалось вернуть настройки — {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Закрыть свой экземпляр (простой 5 минут, выход из программы). Чужой или «усыновлённый» пользователем
    /// (он открыл в нашем скрытом Word свой документ) — только отпустить ссылки. Никогда не бросает.
    /// </summary>
    public void Quit()
    {
        if (App is null) return;
        var pid = _pid;
        var ours = false;
        try
        {
            if (_owned && !_dead)
            {
                if (InUseByUser()) Log($"{Name}: в нашем экземпляре открыты документы пользователя — не закрываем");
                else
                {
                    ours = true;
                    QuitApp();
                    Log($"{Name}: закрыт");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"{Name}: не удалось закрыть — {ex.Message}");
        }
        Detach();
        if (ours) EnsureExited(pid);
    }

    /// <summary>
    /// Завершить процесс нашего экземпляра (зависла задача, таймаут, отмена). Из любого потока.
    /// Чужой экземпляр не трогаем.
    /// </summary>
    public void Kill()
    {
        try
        {
            int pid;
            if (_starting is { } before) pid = SingleNewProcess(before) ?? 0; // завис сам запуск: новый процесс — наш
            else if (_owned) pid = _pid;
            else return;
            if (pid == 0)
            {
                Log($"{Name}: процесс неизвестен — завершить нечего");
                return;
            }
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase)) return;
            process.Kill();
            _dead = true;
            Log($"{Name}: процесс {pid} завершён принудительно");
        }
        catch (Exception ex)
        {
            Log($"{Name}: не удалось завершить процесс — {ex.Message}");
        }
    }

    // ---------- Запуск ----------

    /// <summary>Живой и наш (или чужой, но живой) экземпляр есть — оставляем; умер или занят пользователем — новый.</summary>
    private void EnsureStarted()
    {
        if (App is not null && !_dead)
        {
            try
            {
                var inUse = InUseByUser(); // заодно проверка, что процесс жив (вызов COM)
                if (!_owned || !inUse) return;
                Log($"{Name}: пользователь открыл свои документы в нашем скрытом экземпляре — оставляем ему, запускаем новый");
            }
            catch (Exception ex)
            {
                Log($"{Name}: экземпляр не отвечает ({ex.Message}) — запускаем заново");
            }
        }
        Detach();
        Start();
    }

    protected void Restart()
    {
        Quit();
        EnsureStarted();
    }

    private void Start()
    {
        var type = Type.GetTypeFromProgID(ProgIdOf(Kind), throwOnError: false)
            ?? throw new ConversionException(Loc.F(Loc.T.ErrAppNotFound, Name), ProgIdOf(Kind));
        var watch = Stopwatch.StartNew();
        var before = ProcessIds();
        _starting = before;
        try
        {
            App = Activator.CreateInstance(type) ?? throw new InvalidOperationException("CreateInstance вернул null");
            var pid = SafeFindProcessId() ?? SingleNewProcess(before);
            _owned = pid is { } p ? !before.Contains(p) : ProcessIds().Except(before).Any();
            _pid = pid ?? 0;
            _starting = null;
            if (_owned)
            {
                ConfigureOwned();
                App.DisplayAlerts = QuietAlerts;
                // Обязательно: по умолчанию Office при автоматизации разрешает макросы в открываемых файлах.
                App.AutomationSecurity = ForceDisableMacros;
                _baseline = OpenCount();
            }
            _dead = false;
            _running = true;
            LastUsedUtc = DateTime.UtcNow;
            Log($"{Name} {Version()}: запущен за {watch.ElapsedMilliseconds} мс, процесс {pid?.ToString() ?? "?"}"
                + (_owned ? "" : " — уже был открыт пользователем, его не закрываем"));
        }
        catch (Exception ex)
        {
            Log($"{Name}: не удалось запустить — {ex}");
            // Не настроили (например, не запретили макросы) — в таком экземпляре работать нельзя.
            var pid = _pid;
            var ours = _owned;
            if (ours)
            {
                try { QuitApp(); }
                catch { /* добьём ниже */ }
            }
            Detach();
            if (ours) EnsureExited(pid);
            throw ex as ConversionException ?? new ConversionException(Loc.F(Loc.T.ErrCantStart, Name), ex.Message, ex);
        }
        finally
        {
            _starting = null;
        }
    }

    private int? SafeFindProcessId()
    {
        try { return FindProcessId(); }
        catch (Exception ex)
        {
            Log($"{Name}: окно приложения не найдено ({ex.Message}) — ищем процесс по списку");
            return null;
        }
    }

    private string Version()
    {
        try { return (string)App!.Version; }
        catch { return ""; }
    }

    /// <summary>Отпустить ссылки на экземпляр (не закрывая его).</summary>
    private void Detach()
    {
        object? app = App;
        App = null;
        _running = false;
        _dead = false;
        _owned = false;
        _pid = 0;
        Release(app);
    }

    private void EnsureExited(int pid)
    {
        // Excel не выходит, пока живы ссылки на его объекты: добираем «неявные» обёртки COM сборкой мусора.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (pid == 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.WaitForExit(QuitWait)) return;
            if (!process.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase)) return;
            process.Kill();
            Log($"{Name}: не вышел за {QuitWait.TotalSeconds:0} с — процесс {pid} завершён");
        }
        catch (ArgumentException) { /* уже вышел */ }
        catch (Exception ex) { Log($"{Name}: процесс {pid} — {ex.Message}"); }
    }

    // ---------- Помощники ----------

    private bool InUseByUser() => OpenCount() > _baseline || IsVisible();

    private int OpenCount()
    {
        dynamic docs = Documents();
        try { return (int)docs.Count; }
        finally { Release((object?)docs); }
    }

    private bool IsOpen(string path)
    {
        dynamic docs = Documents();
        try
        {
            int count = docs.Count;
            for (var i = 1; i <= count; i++)
            {
                dynamic doc = docs[i];
                try
                {
                    if (string.Equals((string)doc.FullName, path, StringComparison.OrdinalIgnoreCase)) return true;
                }
                finally { Release((object?)doc); }
            }
            return false;
        }
        finally { Release((object?)docs); }
    }

    private HashSet<int> ProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(ProcessName))
        {
            ids.Add(process.Id);
            process.Dispose();
        }
        return ids;
    }

    private int? SingleNewProcess(HashSet<int> before)
    {
        var fresh = ProcessIds();
        fresh.ExceptWith(before);
        return fresh.Count == 1 ? fresh.First() : null;
    }

    protected static int? PidOfWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        OfficeNative.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : (int)pid;
    }

    protected static void Release(object? com)
    {
        try
        {
            if (com is not null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com);
        }
        catch { /* уже освобождён */ }
    }

    /// <summary>Шаг конвертации: ошибка Office → понятное сообщение (подробности — в лог).</summary>
    protected T Step<T>(string message, Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            throw Translate(ex, message);
        }
    }

    protected void Step(string message, Action action) =>
        Step<object?>(message, () =>
        {
            action();
            return null;
        });

    private ConversionException Translate(Exception ex, string message)
    {
        var details = $"{Name}: 0x{ex.HResult:X8} {ex.GetType().Name}: {ex.Message}";
        if (IsPasswordError(ex)) return new ConversionException(Loc.T.ErrDocPassword, details, ex);
        if (IsServerGone(ex))
        {
            _dead = true; // в следующий раз — новый экземпляр
            return new ConversionException(Loc.F(Loc.T.ErrAppClosed, Name), details, ex);
        }
        return new ConversionException(message, details, ex);
    }

    private static bool IsPasswordError(Exception ex) =>
        (uint)ex.HResult == 0x800A1520 // Word, ошибка 5408: неверный пароль
        || ex.Message.Contains("парол", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

    /// <summary>Процесс Office закрыли или он упал: RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED, RPC_E_DISCONNECTED…</summary>
    private static bool IsServerGone(Exception ex) =>
        ex is InvalidComObjectException
        || (uint)ex.HResult is 0x800706BA or 0x800706BE or 0x80010108 or 0x80010007 or 0x80010012 or 0x800401FD;
}

/// <summary>Word: Documents.Open только для чтения, SaveAs2 / ExportAsFixedFormat, закрыть без сохранения.</summary>
internal sealed class WordHost(Action<string> log) : OfficeHost(log)
{
    private const int AlertsNone = 0;              // wdAlertsNone
    private const int DoNotSaveChanges = 0;        // wdDoNotSaveChanges
    private const int ExportOptimizeForPrint = 0;  // wdExportOptimizeForPrint
    private const int ExportHeadingBookmarks = 1;  // wdExportCreateHeadingBookmarks: оглавление PDF по заголовкам
    private const string PdfWarningValue = "DisableConvertPdfWarning";

    public override OfficeApp Kind => OfficeApp.Word;
    protected override string ProcessName => "WINWORD";
    protected override object QuietAlerts => AlertsNone;
    protected override dynamic Documents() => App!.Documents;
    protected override bool IsVisible() => (bool)App!.Visible;

    /// <summary>
    /// У Word нет свойства Hwnd (KB 258511): временно даём окну уникальный заголовок,
    /// находим окно класса OpusApp и по нему — процесс.
    /// </summary>
    protected override int? FindProcessId()
    {
        string caption = App!.Caption;
        var marker = "DropWheel " + Guid.NewGuid().ToString("N");
        App.Caption = marker;
        try { return PidOfWindow(OfficeNative.FindWindow("OpusApp", marker)); }
        finally { App.Caption = caption; }
    }

    protected override void ConfigureOwned()
    {
        App!.Visible = false;
        App.ScreenUpdating = false;
    }

    protected override void QuitApp()
    {
        try { App!.NormalTemplate.Saved = true; } // не спрашивать про Normal.dotm
        catch { /* шаблона может не быть */ }
        App!.Quit(SaveChanges: DoNotSaveChanges);
    }

    protected override void Convert(OfficeTarget target, string input, string output, IProgress<double>? progress, CancellationToken ct)
    {
        if (Extensions.Normalize(Path.GetExtension(input)) == "pdf") PreparePdfReflow();

        dynamic docs = App!.Documents;
        object? document = null;
        try
        {
            // Только чтение, без «последних файлов», без вопросов о кодировке и преобразовании;
            // пароль-заглушка — для защищённого файла ошибка вместо окна.
            var opened = Step<object>(Loc.F(Loc.T.ErrAppCantOpen, "Word"), () => docs.Open(
                FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                PasswordDocument: DummyPassword, Revert: false, Visible: false, OpenAndRepair: false,
                NoEncodingDialog: true));
            document = opened;
            progress?.Report(0.5);
            ct.ThrowIfCancellationRequested();

            dynamic doc = opened;
            Step(Loc.F(Loc.T.ErrAppCantSave, "Word"), () =>
            {
                switch (target.Action)
                {
                    case OfficeAction.ExportPdf:
                        doc.ExportAsFixedFormat(OutputFileName: output, ExportFormat: target.Format, OpenAfterExport: false,
                            OptimizeFor: ExportOptimizeForPrint, CreateBookmarks: ExportHeadingBookmarks, DocStructureTags: true);
                        break;
                    case OfficeAction.SaveAs when target.Format == OfficeFormats.WordUnicodeText:
                        doc.SaveAs2(FileName: output, FileFormat: target.Format, AddToRecentFiles: false,
                            Encoding: OfficeFormats.Utf8CodePage);
                        break;
                    case OfficeAction.SaveAs:
                        doc.SaveAs2(FileName: output, FileFormat: target.Format, AddToRecentFiles: false);
                        break;
                    default:
                        throw new ConversionException(Loc.F(Loc.T.ErrCantDo, "Word", target.Action));
                }
            });
        }
        finally
        {
            if (document is not null)
            {
                try { ((dynamic)document).Close(SaveChanges: DoNotSaveChanges); }
                catch (Exception ex) { Log("Word: документ не закрылся — " + ex.Message); }
            }
            Release(document);
            Release((object?)docs);
        }
    }

    /// <summary>
    /// Открывая PDF, Word спрашивает «Word преобразует PDF в редактируемый документ…» — даже без окон, и задача
    /// повисла бы. Отключаем вопрос так же, как галочка «Больше не показывать»:
    /// HKCU\Software\Microsoft\Office\{версия}\Word\Options, DWORD DisableConvertPdfWarning = 1
    /// (настройка пользователя, возвращать не нужно). Word читает её при запуске — после первой записи
    /// свой экземпляр перезапускаем.
    /// </summary>
    private void PreparePdfReflow()
    {
        bool changed;
        try
        {
            string version = App!.Version; // «16.0» — Office 2016/2019/2021/365, «15.0» — 2013
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Office\{version}\Word\Options");
            changed = key.GetValue(PdfWarningValue) is not int value || value != 1;
            if (changed) key.SetValue(PdfWarningValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Log("Word: не удалось отключить вопрос о PDF — " + ex.Message);
            return;
        }
        if (changed && IsOwned)
        {
            Log("Word: отключён вопрос о преобразовании PDF — перезапускаем Word, чтобы он это прочитал");
            Restart();
        }
    }
}

/// <summary>Excel: Workbooks.Open только для чтения, SaveAs / ExportAsFixedFormat, закрыть без сохранения.</summary>
internal sealed class ExcelHost(Action<string> log) : OfficeHost(log)
{
    private const int UpdateLinksNever = 0;
    private const int QualityStandard = 0; // xlQualityStandard

    public override OfficeApp Kind => OfficeApp.Excel;
    protected override string ProcessName => "EXCEL";
    protected override object QuietAlerts => false;
    protected override dynamic Documents() => App!.Workbooks;
    protected override bool IsVisible() => (bool)App!.Visible;
    protected override int? FindProcessId() => PidOfWindow(new IntPtr((int)App!.Hwnd));

    protected override void ConfigureOwned()
    {
        App!.Visible = false;
        App.ScreenUpdating = false;
        App.EnableEvents = false; // события книг не срабатывают
    }

    protected override void QuitApp() => App!.Quit();

    protected override void Convert(OfficeTarget target, string input, string output, IProgress<double>? progress, CancellationToken ct)
    {
        dynamic books = App!.Workbooks;
        object? workbook = null;
        try
        {
            // Связи с другими файлами не обновлять; Local — CSV читается и пишется с разделителем
            // из региональных настроек Windows (у русской — «;»), как при открытии файла двойным щелчком.
            var opened = Step<object>(Loc.F(Loc.T.ErrAppCantOpen, "Excel"), () => books.Open(
                Filename: input, UpdateLinks: UpdateLinksNever, ReadOnly: true, Password: DummyPassword,
                IgnoreReadOnlyRecommended: true, Notify: false, AddToMru: false, Local: true));
            workbook = opened;
            progress?.Report(0.5);
            ct.ThrowIfCancellationRequested();

            dynamic wb = opened;
            Step(Loc.F(Loc.T.ErrAppCantSave, "Excel"), () =>
            {
                switch (target.Action)
                {
                    case OfficeAction.ExportPdf: // вся книга, с учётом областей печати
                        wb.ExportAsFixedFormat(Type: target.Format, Filename: output, Quality: QualityStandard,
                            IncludeDocProperties: true, IgnorePrintAreas: false, OpenAfterPublish: false);
                        break;
                    case OfficeAction.SaveAs when target.Format == OfficeFormats.ExcelCsvUtf8:
                        SaveCsv(opened, output);
                        break;
                    case OfficeAction.SaveAs:
                        wb.SaveAs(Filename: output, FileFormat: target.Format, AddToMru: false);
                        break;
                    default:
                        throw new ConversionException(Loc.F(Loc.T.ErrCantDo, "Excel", target.Action));
                }
            });
        }
        finally
        {
            if (workbook is not null)
            {
                try { ((dynamic)workbook).Close(SaveChanges: false); }
                catch (Exception ex) { Log("Excel: книга не закрылась — " + ex.Message); }
            }
            Release(workbook);
            Release((object?)books);
        }
    }

    /// <summary>
    /// CSV — один лист, активный. Делаем активным первый (а не тот, на котором книгу закрыли).
    /// CSV UTF-8 появился в Excel 2016; если его нет — CSV в кодировке Windows (кириллица 1251).
    /// </summary>
    private void SaveCsv(object workbook, string output)
    {
        dynamic wb = workbook;
        dynamic sheets = wb.Worksheets;
        try
        {
            dynamic first = sheets[1];
            try { first.Activate(); }
            finally { Release((object?)first); }
        }
        catch (Exception ex) { Log($"Excel: первый лист не выбран ({ex.Message}) — сохраняем активный"); }
        finally { Release((object?)sheets); }

        try
        {
            wb.SaveAs(Filename: output, FileFormat: OfficeFormats.ExcelCsvUtf8, AddToMru: false, Local: true);
        }
        catch (COMException ex)
        {
            Log($"Excel: CSV UTF-8 не сохранился ({ex.Message}) — сохраняем CSV в кодировке Windows");
            wb.SaveAs(Filename: output, FileFormat: OfficeFormats.ExcelCsvAnsi, AddToMru: false, Local: true);
        }
    }
}

/// <summary>PowerPoint: Presentations.Open без окна, SaveAs; слайды — каждый в свой PNG.</summary>
internal sealed class PowerPointHost(Action<string> log) : OfficeHost(log)
{
    private const int AlertsNone = 1; // ppAlertsNone
    /// <summary>Ширина PNG слайда; высота — по пропорциям слайда (16:9 → 1080).</summary>
    private const int SlideWidthPx = 1920;

    public override OfficeApp Kind => OfficeApp.PowerPoint;
    protected override string ProcessName => "POWERPNT";
    protected override object QuietAlerts => AlertsNone;
    protected override dynamic Documents() => App!.Presentations;
    protected override bool IsVisible() => (int)App!.Visible == MsoTrue;
    protected override int? FindProcessId() => PidOfWindow(new IntPtr((int)App!.HWND));
    // ConfigureOwned не нужен: PowerPoint не разрешает прятать окно приложения (Visible = false — ошибка),
    // зато презентации открываем без окна (WithWindow = false) — на экране ничего не появляется.

    protected override void QuitApp() => App!.Quit();

    protected override void Convert(OfficeTarget target, string input, string output, IProgress<double>? progress, CancellationToken ct)
    {
        dynamic presentations = App!.Presentations;
        object? presentation = null;
        try
        {
            var opened = Step<object>(Loc.F(Loc.T.ErrAppCantOpen, "PowerPoint"), () => presentations.Open(
                FileName: input, ReadOnly: MsoTrue, Untitled: MsoFalse, WithWindow: MsoFalse));
            presentation = opened;
            progress?.Report(0.3);
            ct.ThrowIfCancellationRequested();

            if (target.Action == OfficeAction.SlidesToPng)
                ExportSlides(opened, output, progress, ct);
            else
            {
                dynamic p = opened;
                Step(Loc.F(Loc.T.ErrAppCantSave, "PowerPoint"), () => { p.SaveAs(FileName: output, FileFormat: target.Format); });
            }
        }
        finally
        {
            if (presentation is not null)
            {
                try
                {
                    dynamic p = presentation;
                    p.Saved = MsoTrue; // закрыть без вопроса о сохранении
                    p.Close();
                }
                catch (Exception ex) { Log("PowerPoint: презентация не закрылась — " + ex.Message); }
            }
            Release(presentation);
            Release((object?)presentations);
        }
    }

    /// <summary>Каждый слайд — Slide.Export прямо в нашу временную папку: слайд-1.png, слайд-2.png…</summary>
    private void ExportSlides(object presentation, string folder, IProgress<double>? progress, CancellationToken ct)
    {
        dynamic p = presentation;
        var (width, height) = Step(Loc.T.ErrSlideSize, () =>
        {
            dynamic setup = p.PageSetup;
            try
            {
                double w = setup.SlideWidth, h = setup.SlideHeight;
                return w > 0 && h > 0
                    ? (SlideWidthPx, (int)Math.Round(SlideWidthPx * h / w))
                    : (SlideWidthPx, SlideWidthPx * 9 / 16);
            }
            finally { Release((object?)setup); }
        });

        dynamic slides = p.Slides;
        try
        {
            int count = slides.Count;
            if (count == 0) throw new ConversionException(Loc.T.ErrNoSlides);
            for (var i = 1; i <= count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var number = i;
                var file = Path.Combine(folder, OfficeFormats.SlideFileName(number, count));
                Step(Loc.F(Loc.T.ErrSlideSave, number), () =>
                {
                    dynamic slide = slides[number];
                    try { slide.Export(FileName: file, FilterName: "PNG", ScaleWidth: width, ScaleHeight: height); }
                    finally { Release((object?)slide); }
                });
                progress?.Report(0.3 + 0.7 * number / count);
            }
        }
        finally { Release((object?)slides); }
    }
}

internal static partial class OfficeNative
{
    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindow(string? className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
