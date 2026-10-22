using System.Windows;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Input;
using DropWheel.Ipc;
using DropWheel.Jobs;
using DropWheel.Logging;
using DropWheel.Settings;
using DropWheel.Shell;
using DropWheel.Updates;
using DropWheel.Wheel;
using Microsoft.Toolkit.Uwp.Notifications;
using DropWheel.Localization;

namespace DropWheel.App;

/// <summary>
/// Точка входа. Первый экземпляр живёт в трее и принимает пути от остальных через named pipe;
/// второй (запущенный из меню проводника) передаёт аргументы первому и сразу выходит.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan BatchQuiet = TimeSpan.FromMilliseconds(300);

    private SingleInstance? _instance;
    private PipeServer? _pipe;
    private InvocationBatcher? _batcher;
    private TrayIcon? _tray;
    private Notifier? _notifier;
    private JobsWindow? _jobsWindow;
    private DragWheelController? _dragWheel;
    private Updater? _updater;
    private IReadOnlyList<IInternalEngine> _engines = [];
    private WheelWindow? _chooser;
    private IReadOnlyList<string> _chooserPaths = [];
    /// <summary>Картинки, сохранённые из буфера обмена и браузера в этом сеансе, — их можно удалить после конвертации.</summary>
    private readonly HashSet<string> _clipboardImages = new(StringComparer.OrdinalIgnoreCase);
    private SettingsWindow? _settingsWindow;
    private SettingsSaver? _saver;
    private GlobalHotkey? _clipboardHotkey;
    private const int ClipboardHotkeyId = 1;

    /// <summary>Сколько ждать после последней правки в окне настроек, прежде чем записать settings.json.</summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    internal static FileLog Log { get; private set; } = null!;
    internal AppSettings Settings { get; private set; } = new();
    internal JobQueue Queue { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = CommandLine.Parse(e.Args);

        // Регистрация меню — разовые команды, без трея.
        if (args.Register is not null)
        {
            RunRegistration(args.Register.Value);
            Shutdown();
            return;
        }

        var pipeName = PipeNames.ForCurrentUser("DropWheel");

        // Установщик: закрыть запущенный DropWheel (если есть) и дождаться, пока он отпустит файлы.
        if (args.Exit)
        {
            Environment.ExitCode = ExitRunningInstance(pipeName);
            Shutdown(Environment.ExitCode);
            return;
        }

        _instance = SingleInstance.TryAcquire(pipeName);
        // После обновления старый экземпляр ещё закрывается — подождём его, а не отдадим ему аргументы.
        for (var i = 0; _instance is null && args.AfterUpdate is not null && i < 50; i++)
        {
            Thread.Sleep(200);
            _instance = SingleInstance.TryAcquire(pipeName);
        }
        if (_instance is null)
        {
            // Разрешаем первому экземпляру вывести меню поверх проводника.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            if (!ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
                SendToFirstInstance(pipeName, e.Args);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(AppPaths.DataDir);
        Log = new FileLog(AppPaths.LogsDir);
        Log.Write($"Запуск DropWheel {typeof(App).Assembly.GetName().Version}, аргументы: {string.Join(' ', e.Args)}");
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write("Необработанная ошибка: " + ex.Exception);
            ex.Handled = true;
        };

        // Тема, акцент и анимации Windows: колесо и окна перечитывают их при смене настроек.
        Ui.SystemTheme.Subscribe();

        Settings = AppSettings.Load(AppPaths.SettingsFile);
        // Язык интерфейса — до первого окна и значка (D-LANG): настройка или язык Windows.
        Loc.Current = Loc.Resolve(Settings.Language);
        Log.Write($"Язык интерфейса: {Loc.Current} (настройка {Settings.Language}, Windows {System.Globalization.CultureInfo.CurrentUICulture.Name})");
        _saver = new SettingsSaver(AppPaths.SettingsFile, SaveDelay, Log.Write);
        // Автозапуск — то, что записано в реестре (его могли выключить в диспетчере задач).
        AutoStartInstaller.RepairIfMoved(Log);
        Settings = Settings with { AutoStart = AutoStartInstaller.IsEnabled() };
        var tools = new ToolLocator { FfmpegPath = Settings.FfmpegPath, MagickPath = Settings.MagickPath };
        _engines = AppEngines.EngineRegistry.Create(Log, Dispatcher);
        Queue = new JobQueue(new Converter(tools, Settings.Conversion, _engines), maxParallel: 2, Log.Write);

        _notifier = new Notifier(Log);
        _tray = new TrayIcon(this);
        Queue.Changed += job => Dispatcher.BeginInvoke(() => OnJobChanged(job));

        if (Settings.ContextMenuEnabled) ContextMenuInstaller.TryInstall(Log);

        StartDragWheel();
        SetClipboardHotkey(Settings.ClipboardHotkey);
        StartUpdater(args.AfterUpdate);

        _batcher = new InvocationBatcher(BatchQuiet, paths => Dispatcher.BeginInvoke(() => ShowChooser(paths)));
        _pipe = new PipeServer(pipeName, message => HandleArgs(CommandLine.Parse(message), fromPipe: true));
        _pipe.Start();

        // Запуск двойным кликом: окна нет — подскажем, где искать программу. При входе в Windows — молча.
        if (args.ConvertPaths.Count == 0 && !args.AutoStart) _tray.ShowStartHint(Hotkeys.DisplayName(Settings.Hotkey));
        HandleArgs(args, fromPipe: false);
    }

    /// <summary>Колесо по горячей клавише при перетаскивании (этап 3). Сбой хуков не мешает остальному.</summary>
    private void StartDragWheel()
    {
        try
        {
            _dragWheel = new DragWheelController(Settings.Hotkey, Dispatcher, Queue.Converter, Enqueue, EnqueueBrowserImage, Log, () => Settings.Wheel)
            {
                FillPercent = Settings.WheelOpacity,
            };
            _dragWheel.Start();
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить колесо (хуки): " + ex);
            _dragWheel?.Dispose();
            _dragWheel = null;
        }
    }

    /// <summary>
    /// Ctrl+Alt+V — колесо для буфера обмена (D-CLIPBOARD). Сочетание занято другой программой — пишем в журнал
    /// и работаем без него (пункт трея «Из буфера обмена…» остаётся).
    /// </summary>
    private void SetClipboardHotkey(bool on)
    {
        try
        {
            if (!on)
            {
                _clipboardHotkey?.Dispose();
                _clipboardHotkey = null;
                return;
            }
            _clipboardHotkey ??= new GlobalHotkey(ClipboardHotkeyId, FromClipboard);
            if (_clipboardHotkey.IsRegistered) return;
            if (_clipboardHotkey.TryRegister(GlobalHotkey.ModControl | GlobalHotkey.ModAlt, GlobalHotkey.VkV, out var error))
                Log.Write("Ctrl+Alt+V (буфер обмена) включена");
            else
                Log.Write($"Ctrl+Alt+V занята другой программой или недоступна (код {error}) — работаю без неё");
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить Ctrl+Alt+V: " + ex);
        }
    }

    /// <summary>
    /// «Из буфера обмена…» (трей, Ctrl+Alt+V): файлы — колесо для них, как из проводника; картинка — сохранить в PNG
    /// (папка из ClipboardPlan.ImageFolder) и открыть колесо для неё; иначе — подсказка.
    /// </summary>
    internal void FromClipboard()
    {
        ClipboardReader.Snapshot snapshot;
        try
        {
            snapshot = ClipboardReader.Read();
        }
        catch (Exception ex)
        {
            Log.Write("Буфер обмена не прочитался: " + ex.Message);
            _tray?.ShowInfo(Loc.T.ClipboardNothingTitle, Loc.T.ClipboardFailed);
            return;
        }

        var action = ClipboardPlan.Decide(snapshot.Files, snapshot.HasImage);
        switch (action.Kind)
        {
            case ClipboardActionKind.Files:
                Log.Write($"Буфер обмена: файлов {action.Files.Count}");
                ShowChooser(action.Files);
                break;
            case ClipboardActionKind.Image:
                string path;
                try
                {
                    var folder = ClipboardPlan.ImageFolder(Settings.ClipboardFolder, Settings.Conversion);
                    Directory.CreateDirectory(folder);
                    path = ClipboardPlan.ImagePath(folder, DateTime.Now);
                    ClipboardReader.SaveImage(snapshot, path);
                }
                catch (Exception ex)
                {
                    Log.Write("Картинка из буфера не сохранилась: " + ex);
                    _tray?.ShowInfo(Loc.T.ClipboardNothingTitle, Loc.T.ClipboardFailed);
                    return;
                }
                Log.Write($"Буфер обмена: картинка сохранена в {path}");
                _clipboardImages.Add(path);
                ShowChooser([path]);
                break;
            default:
                _tray?.ShowInfo(Loc.T.ClipboardNothingTitle, snapshot.HasImageLink ? Loc.T.ClipboardImageLinkOnly : Loc.T.ClipboardNothing);
                break;
        }
    }

    /// <summary>
    /// Картинку из браузера бросили на сектор колеса (D-BROWSER): сохранить в папку картинок
    /// (та же, что для буфера обмена) под её именем или «Браузер дата время», затем — в очередь.
    /// «Оставлять исходную картинку» действует и на неё.
    /// </summary>
    private void EnqueueBrowserImage(DraggedImage image, Operation op)
    {
        string path;
        try
        {
            var folder = ClipboardPlan.ImageFolder(Settings.ClipboardFolder, Settings.Conversion);
            Directory.CreateDirectory(folder);
            path = BrowserDrag.ImagePath(folder, image, DateTime.Now);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                file.Write(image.Bytes);
        }
        catch (Exception ex)
        {
            Log.Write("Картинка из браузера не сохранилась: " + ex);
            _tray?.ShowInfo(Loc.T.BrowserImageFailed, ex.Message);
            return;
        }
        Log.Write($"Браузер: картинка сохранена в {path}");
        _clipboardImages.Add(path);
        Enqueue([path], op);
    }

    private void StartUpdater(int? afterUpdate)
    {
        var removed = UpdateApplier.CleanupOld(Updater.AppDir);
        if (afterUpdate is { } build)
        {
            Log.Write($"Запуск после обновления до сборки {build}, удалено старых файлов: {removed}");
            _tray?.ShowInfo(Loc.T.UpdatedTitle, Loc.F(Loc.T.UpdatedText, build));
        }

        _updater = new Updater(Dispatcher, Queue, Log,
            onAvailable: m => Dispatcher.BeginInvoke(() => _tray?.SetUpdateAvailable(m.Build)),
            beforeRestart: ReleaseForRestart,
            channel: () => UpdateChannels.Resolve(Settings.UpdateChannel, UpdateChannels.Embedded));
        Log.Write($"Версия {AppVersion.CurrentDisplay ?? "разработчика"}, сборка {AppVersion.CurrentBuild}, " +
                  $"канал при сборке {UpdateChannels.Embedded}, настройка канала {Settings.UpdateChannel}");
        if (Settings.CheckForUpdates) _updater.Start();
    }

    /// <summary>Пункт трея «Проверить обновления».</summary>
    internal async void CheckUpdatesManually()
    {
        if (_updater is null) return;
        try
        {
            if (!await _updater.CheckAsync(manual: true))
                _tray?.ShowInfo(Loc.T.NoUpdatesTitle, Loc.F(Loc.T.NoUpdatesText, AppVersion.CurrentBuild));
        }
        catch (Exception ex)
        {
            _tray?.ShowInfo(Loc.T.UpdateCheckFailed, ex.Message);
        }
    }

    /// <summary>Пункт трея «Обновить до сборки N».</summary>
    internal async void InstallUpdate()
    {
        if (_updater?.Available is not { } manifest) return;
        try
        {
            _tray?.ShowInfo(Loc.T.UpdatingTitle, Loc.F(Loc.T.UpdatingText, manifest.Build));
            // Progress<T> возвращает отчёты в поток интерфейса; ход загрузки — в подсказке значка и в меню трея.
            var status = new Progress<string>(text => _tray?.SetStatus(text));
            await _updater.InstallAsync(manifest, status);
        }
        catch (Exception ex)
        {
            Log.Write("Обновление не удалось: " + ex);
            _tray?.SetStatus(null);
            _tray?.ShowInfo(Loc.T.UpdateFailed, ex.Message);
        }
    }

    /// <summary>Перед перезапуском: отпустить Mutex, канал, хуки и значок, чтобы новый экземпляр стал первым.</summary>
    private void ReleaseForRestart()
    {
        _saver?.Flush();
        _dragWheel?.Dispose();
        _dragWheel = null;
        _clipboardHotkey?.Dispose();
        _clipboardHotkey = null;
        _pipe?.Dispose();
        _pipe = null;
        _instance?.Dispose();
        _instance = null;
        _tray?.Dispose();
        _tray = null;
    }

    private static void SendToFirstInstance(string pipeName, string[] args)
    {
        // Первый экземпляр мог только что стартовать и ещё не открыть канал — несколько попыток.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (PipeClient.TrySend(pipeName, args, timeoutMs: 1000)) return;
            Thread.Sleep(200);
        }
    }

    /// <summary>
    /// «DropWheel.exe --exit»: попросить первый экземпляр закрыться (через канал) и ждать, пока он отпустит Mutex.
    /// Код выхода: 0 — DropWheel не был запущен, 2 — был и закрылся (установщик запустит его снова после
    /// тихого обновления), 1 — не закрылся за отведённое время.
    /// </summary>
    private static int ExitRunningInstance(string pipeName)
    {
        var log = new FileLog(AppPaths.LogsDir);
        using (var probe = SingleInstance.TryAcquire(pipeName))
            if (probe is not null) return 0; // никто не запущен

        log.Write("--exit: прошу запущенный DropWheel закрыться");
        var sent = false;
        for (var attempt = 0; attempt < 5 && !sent; attempt++)
        {
            sent = PipeClient.TrySend(pipeName, [CommandLine.ExitArgument], timeoutMs: 1000);
            if (!sent) Thread.Sleep(200);
        }
        if (!sent) log.Write("--exit: запущенный экземпляр не ответил по каналу (старая версия или завис)");

        // Задачи отменяются, движки (Office) закрываются — даём до 20 секунд.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(sent ? 20 : 2);
        while (DateTime.UtcNow < deadline)
        {
            using var instance = SingleInstance.TryAcquire(pipeName);
            if (instance is not null)
            {
                log.Write("--exit: DropWheel закрыт");
                return 2;
            }
            Thread.Sleep(250);
        }
        log.Write("--exit: DropWheel не закрылся");
        return 1;
    }

    private void HandleArgs(CommandLine args, bool fromPipe)
    {
        if (args.Exit)
        {
            Log.Write("Получена команда --exit (установщик): закрываюсь");
            Dispatcher.BeginInvoke(Quit);
            return;
        }
        if (args.ConvertPaths.Count > 0) _batcher?.Add(args.ConvertPaths);
        // Повторный запуск DropWheel.exe без файлов — показать окно задач.
        else if (fromPipe) Dispatcher.BeginInvoke(ShowJobs);
    }

    /// <summary>
    /// «Конвертировать…» из проводника: то же колесо у курсора, в режиме клика (раздел 9 ТЗ, D16);
    /// выбор — в очередь. Если колесо не открылось — запасное списочное меню.
    /// </summary>
    private void ShowChooser(IReadOnlyList<string> paths)
    {
        Log.Write($"Выбор операции для {paths.Count} файл(ов)");
        var ops = OperationCatalog.ForFiles(paths, Queue.Converter.CanRun);
        Queue.Converter.Prepare(ops);
        try
        {
            var wheel = _chooser ??= CreateChooser();
            var (x, y) = WheelNative.CursorPosition() ?? throw new InvalidOperationException("Не удалось узнать позицию курсора");
            _chooserPaths = paths;
            wheel.CloseOnDeactivate = true;
            var (convert, tools) = WheelArrangement.Arrange(ops, WheelGroups.ForFiles(paths), Settings.Wheel);
            wheel.SetItems(convert, tools, paths);
            wheel.ShowAt(x, y, activate: true);
        }
        catch (Exception ex)
        {
            Log.Write("Колесо выбора не открылось, показываю список: " + ex);
            try { _chooser?.Cancel(); }
            catch (Exception) { }
            ChooserMenu.Show(paths, ops, op => Enqueue(paths, op));
        }
    }

    private WheelWindow CreateChooser()
    {
        var wheel = new WheelWindow { CloseOnDeactivate = true, FillPercent = Settings.WheelOpacity };
        wheel.ItemChosen += id =>
        {
            var paths = _chooserPaths;
            if (OperationCatalog.Find(id) is not { } op || paths.Count == 0) return;
            Log.Write($"Колесо (меню проводника): {op.Id} для {paths.Count} файл(ов)");
            Enqueue(paths, op);
        };
        return wheel;
    }

    /// <summary>Выбор пользователя (колесо, меню проводника) — в очередь; запомнить как «последний выбор» для этого типа.</summary>
    internal void Enqueue(IReadOnlyList<string> paths, Operation op)
    {
        Queue.EnqueueSelection(paths, op);
        var group = WheelGroups.ForFiles(paths);
        UpdateSettings(s => s with { Wheel = s.Wheel.WithRecent(group, op.Mode, op.Id) });
    }

    private void OnJobChanged(Job job)
    {
        _tray?.Update(Queue.Jobs);
        _jobsWindow?.Refresh(job);
        if (job.State == JobState.Done && job.OutputPath is not null)
            _notifier?.Done(job.OutputPath);
        else if (job.State == JobState.Failed)
        {
            Log.Write($"Ошибка {job.Operation.Id} {job.Input}: {job.Error}\n{job.ErrorDetails}");
            _notifier?.Failed(job.Input, job.Error ?? Loc.T.UnknownError);
        }
        if (job.State is JobState.Done or JobState.Failed or JobState.Canceled)
            CleanUpClipboardImage(job);
    }

    /// <summary>
    /// Картинка из буфера или браузера — промежуточный файл: если «Оставлять исходную картинку» выключено и задача удалась,
    /// удаляем его (решение D-CLIPBOARD). Ошибка или отмена — оставляем, чтобы картинка не пропала.
    /// </summary>
    private void CleanUpClipboardImage(Job job)
    {
        if (job.Inputs.Count != 1 || !_clipboardImages.Remove(job.Input)) return;
        if (!ClipboardPlan.ShouldDeleteImage(Settings.KeepClipboardImage, job.State == JobState.Done, job.Input, job.OutputPath))
            return;
        try
        {
            File.Delete(job.Input);
            Log.Write($"Буфер обмена: промежуточная картинка удалена {job.Input}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Буфер обмена: картинку не удалось удалить: " + ex.Message);
        }
    }

    internal void ShowJobs()
    {
        if (_jobsWindow is null)
        {
            _jobsWindow = new JobsWindow(Queue, Log, Input.Hotkeys.DisplayName(Settings.Hotkey));
            _jobsWindow.Closed += (_, _) => _jobsWindow = null;
        }
        _jobsWindow.ShowNearTray();
    }

    /// <summary>Клик по значку в трее: окно задач открыть или спрятать.</summary>
    internal void ToggleJobs()
    {
        if (_jobsWindow is null) ShowJobs();
        else _jobsWindow.Toggle();
    }

    internal void SetContextMenuEnabled(bool enabled) => UpdateSettings(s => s with { ContextMenuEnabled = enabled });

    /// <summary>Окно настроек (пункт трея «Настройки…», двойной клик по значку). Одно на приложение.</summary>
    internal void ShowSettings()
    {
        try
        {
            if (_settingsWindow is null)
            {
                // Автозапуск показываем по реестру — его могли поменять в обход программы.
                Settings = Settings with { AutoStart = AutoStartInstaller.IsEnabled() };
                var window = new SettingsWindow(Settings, change => UpdateSettings(change, fromWindow: true),
                    AppVersion.CurrentBuild, Log, AppVersion.CurrentDisplay, UpdateChannels.Embedded);
                window.View.OpenLogsRequested += () => Notifier.OpenLog(Log);
                window.View.CheckUpdatesRequested += CheckUpdatesManually;
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_settingsWindow, window)) _settingsWindow = null;
                    _saver?.Flush();
                };
                _settingsWindow = window;
            }
            _settingsWindow.ShowAndActivate();
        }
        catch (Exception ex)
        {
            Log.Write("Окно настроек не открылось: " + ex);
        }
    }

    /// <summary>
    /// Единственный способ менять настройки: применить на ходу (клавиша, качество, меню проводника, автозапуск,
    /// обновления), показать в трее и окне настроек и сохранить в settings.json чуть позже.
    /// fromWindow — правка пришла из окна настроек: оно уже показывает её, перестраивать не нужно.
    /// </summary>
    internal AppSettings UpdateSettings(Func<AppSettings, AppSettings> change, bool fromWindow = false)
    {
        var old = Settings;
        var wanted = change(old);
        if (Equals(wanted, old)) return old;

        // Автозапуск: если запись в реестр не удалась — переключатель возвращается как было.
        var now = wanted;
        if (now.AutoStart != old.AutoStart && !AutoStartInstaller.TrySet(now.AutoStart, Log))
            now = now with { AutoStart = old.AutoStart };

        Settings = now;
        Apply(old, now);
        _saver?.Schedule(now);
        _tray?.SyncSettings(now);
        if (!fromWindow || !Equals(now, wanted)) _settingsWindow?.Reload(now);
        return now;
    }

    private void Apply(AppSettings old, AppSettings now)
    {
        if (!string.Equals(now.Hotkey, old.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (_dragWheel is not null) _dragWheel.Hotkey = now.Hotkey;
            if (_jobsWindow is not null) _jobsWindow.HotkeyName = Hotkeys.DisplayName(now.Hotkey);
            Log.Write($"Горячая клавиша: {now.Hotkey}");
        }
        if (!Equals(now.Conversion, old.Conversion))
        {
            Queue.Converter.Settings = now.Conversion;
            Log.Write($"Качество и папка: JPG {now.Conversion.JpegQuality}, WebP {now.Conversion.WebpQuality}, " +
                      $"CRF {now.Conversion.VideoCrf}, MP3 {now.Conversion.Mp3BitrateKbps}, папка {now.Conversion.OutputFolder ?? "рядом"}");
        }
        if (now.ContextMenuEnabled != old.ContextMenuEnabled)
        {
            if (now.ContextMenuEnabled) ContextMenuInstaller.TryInstall(Log);
            else ContextMenuInstaller.TryUninstall(Log);
        }
        if (now.WheelOpacity != old.WheelOpacity)
        {
            if (_dragWheel is not null) _dragWheel.FillPercent = now.WheelOpacity;
            if (_chooser is not null) _chooser.FillPercent = now.WheelOpacity;
        }
        if (now.ClipboardHotkey != old.ClipboardHotkey) SetClipboardHotkey(now.ClipboardHotkey);
        if (now.CheckForUpdates != old.CheckForUpdates)
        {
            if (now.CheckForUpdates) _updater?.Start();
            else _updater?.Stop();
        }
        if (now.Language != old.Language) ApplyLanguage(now);
        if (now.UpdateChannel != old.UpdateChannel)
        {
            Log.Write($"Канал обновлений: {now.UpdateChannel} → {UpdateChannels.Resolve(now.UpdateChannel, UpdateChannels.Embedded)}");
            // Найденное в другом канале обновление больше не предлагаем; новый канал проверим сразу.
            _updater?.Forget();
            if (now.CheckForUpdates && _updater is { } updater) _ = updater.CheckAsync(manual: false);
        }
    }

    /// <summary>Сменили язык в настройках: трей, окна и пункт меню проводника — сразу на новом языке.</summary>
    private void ApplyLanguage(AppSettings now)
    {
        var language = Loc.Resolve(now.Language);
        Log.Write($"Язык интерфейса: {language} (настройка {now.Language})");
        if (language == Loc.Current) return;
        Loc.Current = language;
        _tray?.Relocalize();
        _settingsWindow?.Relocalize();
        _jobsWindow?.Relocalize(Hotkeys.DisplayName(now.Hotkey));
        if (now.ContextMenuEnabled) ContextMenuInstaller.TryInstall(Log);
    }

    internal void Quit()
    {
        foreach (var job in Queue.Jobs) job.Cancel();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _saver?.Dispose();
        _settingsWindow?.Close();
        _updater?.Dispose();
        _dragWheel?.Dispose();
        _clipboardHotkey?.Dispose();
        _chooser?.Close();
        _pipe?.Dispose();
        _batcher?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        // Движки со своими процессами (Office): закрыть, иначе останутся скрытые WINWORD.EXE.
        foreach (var engine in _engines.OfType<IDisposable>())
        {
            try { engine.Dispose(); }
            catch (Exception ex) { Log.Write("Ошибка при закрытии движка: " + ex.Message); }
        }
        base.OnExit(e);
    }

    private static void RunRegistration(bool install)
    {
        var log = new FileLog(AppPaths.LogsDir);
        // Подпись пункта меню — на языке интерфейса.
        Loc.Current = Loc.Resolve(AppSettings.Load(AppPaths.SettingsFile).Language);
        if (install) ContextMenuInstaller.TryInstall(log);
        else ContextMenuInstaller.TryUninstall(log);
    }
}
