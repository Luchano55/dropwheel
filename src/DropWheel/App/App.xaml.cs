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
    private SettingsWindow? _settingsWindow;
    private SettingsSaver? _saver;

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
            _dragWheel = new DragWheelController(Settings.Hotkey, Dispatcher, Queue.Converter, Enqueue, Log, () => Settings.Wheel);
            _dragWheel.Start();
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить колесо (хуки): " + ex);
            _dragWheel?.Dispose();
            _dragWheel = null;
        }
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
            beforeRestart: ReleaseForRestart);
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

    private void HandleArgs(CommandLine args, bool fromPipe)
    {
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
        var wheel = new WheelWindow { CloseOnDeactivate = true };
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
                    AppVersion.CurrentBuild, Log);
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
        if (now.CheckForUpdates != old.CheckForUpdates)
        {
            if (now.CheckForUpdates) _updater?.Start();
            else _updater?.Stop();
        }
        if (now.Language != old.Language) ApplyLanguage(now);
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
