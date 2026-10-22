using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Localization;

namespace DropWheel.AppEngines;

/// <summary>
/// Документы Word / Excel / PowerPoint через установленный Microsoft Office (COM, позднее связывание).
/// <para>
/// Чтобы было «мгновенно»: Office долго запускается (3–5 с), поэтому
/// • Prepare (колесо/меню показали операции Office) запускает нужное приложение заранее, невидимым;
/// • запущенное приложение живёт, пока им пользуются, и закрывается через 5 минут простоя;
/// • при выходе из DropWheel (Dispose) закрывается всё, что мы запустили.
/// </para>
/// <para>
/// Все вызовы COM — в одном отдельном STA-потоке со своей очередью (StaWorker): задачи Office идут по одной.
/// У каждой задачи сторож: дольше 2 минут (PDF → DOCX и слайды — 5) или отмена — процесс Office, который
/// мы запустили, убивается, следующая задача запустит новый. Сбой Office не роняет программу:
/// все ошибки превращаются в ConversionException с понятным текстом.
/// </para>
/// </summary>
public sealed class OfficeEngine : IInternalEngine, IDisposable
{
    /// <summary>Закрыть приложение Office, если им не пользовались столько времени.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    /// <summary>Самая долгая обычная конвертация.</summary>
    public static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(2);
    /// <summary>PDF → DOCX (Word перестраивает PDF) и слайды в PNG — дольше.</summary>
    public static readonly TimeSpan SlowJobTimeout = TimeSpan.FromMinutes(5);
    /// <summary>Прогрев (запуск без документа) и закрытие по простою.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan IdleCheckPeriod = TimeSpan.FromSeconds(30);
    /// <summary>Сколько ждать закрытия Office при выходе из программы.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(15);

    private readonly Action<string> _log;
    private readonly Dictionary<OfficeApp, Lazy<bool>> _installed;
    private readonly object _lock = new();
    private readonly List<Session> _abandoned = [];
    private Session? _session;
    private Timer? _idleTimer;
    private bool _disposed;

    public OfficeEngine(Action<string>? log = null)
    {
        _log = message =>
        {
            try { log?.Invoke(message); }
            catch { /* лог не должен ронять Office */ }
        };
        // Проверка — чтение реестра (ProgID → CLSID), Office не запускается. Один раз за время работы.
        _installed = Enum.GetValues<OfficeApp>().ToDictionary(app => app, app => new Lazy<bool>(() =>
        {
            var found = OfficeHost.IsInstalled(app);
            _log($"Office: {app} {(found ? "установлен" : "не найден")}");
            return found;
        }));
    }

    public EngineKind Kind => EngineKind.Office;

    public bool IsAvailable => _installed.Keys.Any(IsInstalled);

    public bool IsInstalled(OfficeApp app) => _installed[app].Value;

    /// <summary>Операция из блока OFFICE, и нужное ей приложение установлено.</summary>
    public bool CanRun(Operation op) => OfficeFormats.For(op) is { } target && IsInstalled(target.App);

    /// <summary>
    /// Колесо или меню показали операции — запустить нужные приложения заранее (в потоке Office, не блокируя UI).
    /// Уже запущенное приложение только отмечается как «используется» (откладывает закрытие по простою).
    /// </summary>
    public void Prepare(IReadOnlyList<Operation> offered)
    {
        try
        {
            var apps = offered.Select(OfficeFormats.For).OfType<OfficeTarget>()
                .Select(t => t.App).Distinct().Where(IsInstalled).ToList();
            if (apps.Count == 0) return;
            var session = CurrentSession();
            foreach (var app in apps) Prewarm(session, session.Hosts[app]);
        }
        catch (Exception ex)
        {
            _log("Office: прогрев не удался — " + ex.Message);
        }
    }

    public async Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        var target = OfficeFormats.For(op) ?? throw new ConversionException(Loc.F(Loc.T.ErrCantDo, "Office", Loc.Label(op)), op.Id);
        if (!IsInstalled(target.App)) throw new ConversionException(Loc.F(Loc.T.ErrAppNotFound, target.App));
        var input = inputs[0];
        if (OfficeFormats.IsPasswordProtected(input)) throw new ConversionException(Loc.T.ErrDocPassword, input);

        var session = CurrentSession();
        var host = session.Hosts[target.App];
        var timeout = op.Kind == MediaKind.Pdf || op.Result == ResultKind.Folder ? SlowJobTimeout : JobTimeout;
        Interlocked.Increment(ref host.Pending);
        try
        {
            await session.Worker.RunAsync(token => host.Run(target, input, output, progress, token), timeout, host.Kill, ct)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new ConversionException(Loc.F(Loc.T.ErrAppTimeout, host.Name, timeout.TotalMinutes.ToString("0", System.Globalization.CultureInfo.InvariantCulture)), input, ex);
        }
        catch (StaWorkerStoppedException ex)
        {
            throw new ConversionException(Loc.T.ErrOfficeNotResponding, input, ex);
        }
        finally
        {
            Interlocked.Decrement(ref host.Pending);
        }
    }

    /// <summary>Выход из программы: закрыть всё, что мы запустили (не дольше ExitWait), остальное — завершить.</summary>
    public void Dispose()
    {
        Session? session;
        List<Session> abandoned;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            session = _session;
            _session = null;
            abandoned = [.. _abandoned];
        }
        _idleTimer?.Dispose();
        if (session is not null)
        {
            session.Worker.Complete(); // доделает очередь, затем закроет Office (onStop)
            if (!session.Worker.Join(ExitWait))
            {
                _log("Office: не закрылся вовремя — завершаем процессы");
                session.KillOwned();
            }
        }
        foreach (var stuck in abandoned) stuck.KillOwned();
    }

    /// <summary>Процесс нашего экземпляра приложения (0 — не запущен или чужой). Для тестов.</summary>
    internal int ProcessIdOf(OfficeApp app)
    {
        lock (_lock) return _session?.Hosts[app].OwnedProcessId ?? 0;
    }

    internal bool IsRunning(OfficeApp app)
    {
        lock (_lock) return _session?.Hosts[app].IsRunning ?? false;
    }

    // ---------- Внутреннее ----------

    /// <summary>Поток Office и приложения в нём. Зависший поток бросается, следующая задача получает новый.</summary>
    private Session CurrentSession()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { Worker.IsStuck: true } stuck)
            {
                _log("Office: поток завис — начинаем с нового");
                _abandoned.Add(stuck);
                _session = null;
            }
            if (_session is null)
            {
                _session = new Session(_log);
                _idleTimer ??= new Timer(_ => OnIdleTick(), null, IdleCheckPeriod, IdleCheckPeriod);
            }
            return _session;
        }
    }

    private void Prewarm(Session session, OfficeHost host)
    {
        if (Interlocked.Exchange(ref host.PrewarmQueued, 1) == 1) return; // уже в очереди
        session.Worker.RunAsync(_ =>
            {
                Volatile.Write(ref host.PrewarmQueued, 0);
                host.Prewarm();
            }, StartTimeout, host.Kill, CancellationToken.None)
            .ContinueWith(t => _log($"{host.Name}: прогрев не удался — {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    /// <summary>Раз в 30 с (таймер пула потоков): закрыть приложения, простаивающие дольше IdleTimeout.</summary>
    private void OnIdleTick()
    {
        try
        {
            Session? session;
            lock (_lock) session = _disposed ? null : _session;
            if (session is null || !session.Hosts.Values.Any(h => h.IsRunning)) return;
            if (Interlocked.Exchange(ref session.IdleCheckQueued, 1) == 1) return;
            session.Worker.RunAsync(_ =>
                {
                    Volatile.Write(ref session.IdleCheckQueued, 0);
                    foreach (var host in session.Hosts.Values)
                        if (host.IsRunning && Volatile.Read(ref host.Pending) == 0
                            && DateTime.UtcNow - host.LastUsedUtc > IdleTimeout)
                        {
                            _log($"{host.Name}: не нужен {IdleTimeout.TotalMinutes:0} мин — закрываем");
                            host.Quit();
                        }
                }, StartTimeout, session.KillOwned, CancellationToken.None)
                .ContinueWith(t => _log("Office: закрытие по простою — " + t.Exception?.GetBaseException().Message),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _log("Office: проверка простоя — " + ex.Message);
        }
    }

    private sealed class Session
    {
        public readonly StaWorker Worker;
        public readonly IReadOnlyDictionary<OfficeApp, OfficeHost> Hosts;
        public int IdleCheckQueued;

        public Session(Action<string> log)
        {
            Hosts = new Dictionary<OfficeApp, OfficeHost>
            {
                [OfficeApp.Word] = new WordHost(log),
                [OfficeApp.Excel] = new ExcelHost(log),
                [OfficeApp.PowerPoint] = new PowerPointHost(log),
            };
            Worker = new StaWorker("DropWheel Office",
                onStart: () => ComMessageFilter.Register(log),
                onStop: () =>
                {
                    foreach (var host in Hosts.Values) host.Quit();
                    ComMessageFilter.Revoke();
                },
                log: log);
        }

        /// <summary>Последнее средство (поток завис, выход из программы): завершить процессы, которые запустили мы.</summary>
        public void KillOwned()
        {
            foreach (var host in Hosts.Values) host.Kill();
        }
    }
}
