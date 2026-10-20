using System.Windows;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Ipc;
using DropWheel.Jobs;
using DropWheel.Logging;
using DropWheel.Settings;
using DropWheel.Shell;
using DropWheel.Wheel;
using Microsoft.Toolkit.Uwp.Notifications;

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

        Settings = AppSettings.Load(AppPaths.SettingsFile);
        var tools = new ToolLocator { FfmpegPath = Settings.FfmpegPath, MagickPath = Settings.MagickPath };
        Queue = new JobQueue(new Converter(tools, Settings.Conversion), maxParallel: 2, Log.Write);

        _notifier = new Notifier(Log);
        _tray = new TrayIcon(this);
        Queue.Changed += job => Dispatcher.BeginInvoke(() => OnJobChanged(job));

        if (Settings.ContextMenuEnabled) ContextMenuInstaller.TryInstall(Log);

        StartDragWheel();

        _batcher = new InvocationBatcher(BatchQuiet, paths => Dispatcher.BeginInvoke(() => ShowChooser(paths)));
        _pipe = new PipeServer(pipeName, message => HandleArgs(CommandLine.Parse(message), fromPipe: true));
        _pipe.Start();

        // Запуск двойным кликом: окна нет — подскажем, где искать программу.
        if (args.ConvertPaths.Count == 0) _tray.ShowStartHint();
        HandleArgs(args, fromPipe: false);
    }

    /// <summary>Колесо по горячей клавише при перетаскивании (этап 3). Сбой хуков не мешает остальному.</summary>
    private void StartDragWheel()
    {
        try
        {
            _dragWheel = new DragWheelController(Settings.Hotkey, Dispatcher, Queue.Converter, Enqueue, Log);
            _dragWheel.Start();
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить колесо (хуки): " + ex);
            _dragWheel?.Dispose();
            _dragWheel = null;
        }
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

    /// <summary>Меню у курсора для путей из проводника; выбор — в очередь.</summary>
    private void ShowChooser(IReadOnlyList<string> paths)
    {
        Log.Write($"Выбор операции для {paths.Count} файл(ов)");
        var ops = OperationCatalog.ForFiles(paths, Queue.Converter.CanRun);
        Queue.Converter.Prepare(ops);
        ChooserMenu.Show(paths, ops, op => Enqueue(paths, op));
    }

    internal void Enqueue(IReadOnlyList<string> paths, Operation op) => Queue.EnqueueSelection(paths, op);

    private void OnJobChanged(Job job)
    {
        _tray?.Update(Queue.Jobs);
        _jobsWindow?.Refresh(job);
        if (job.State == JobState.Done && job.OutputPath is not null)
            _notifier?.Done(job.OutputPath);
        else if (job.State == JobState.Failed)
        {
            Log.Write($"Ошибка {job.Operation.Id} {job.Input}: {job.Error}\n{job.ErrorDetails}");
            _notifier?.Failed(job.Input, job.Error ?? "Неизвестная ошибка");
        }
    }

    internal void ShowJobs()
    {
        if (_jobsWindow is null)
        {
            _jobsWindow = new JobsWindow(Queue);
            _jobsWindow.Closed += (_, _) => _jobsWindow = null;
        }
        _jobsWindow.ShowNearTray();
    }

    internal void SetContextMenuEnabled(bool enabled)
    {
        if (enabled) ContextMenuInstaller.TryInstall(Log);
        else ContextMenuInstaller.TryUninstall(Log);
        Settings = Settings with { ContextMenuEnabled = enabled };
        try { Settings.Save(AppPaths.SettingsFile); }
        catch (Exception ex) { Log.Write("Не удалось сохранить настройки: " + ex.Message); }
    }

    internal void Quit()
    {
        foreach (var job in Queue.Jobs) job.Cancel();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dragWheel?.Dispose();
        _pipe?.Dispose();
        _batcher?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void RunRegistration(bool install)
    {
        var log = new FileLog(AppPaths.LogsDir);
        if (install) ContextMenuInstaller.TryInstall(log);
        else ContextMenuInstaller.TryUninstall(log);
    }
}
