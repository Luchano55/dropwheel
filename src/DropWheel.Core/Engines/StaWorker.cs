using System.Collections.Concurrent;

namespace DropWheel.Engines;

/// <summary>Задача не выполнена: поток остановлен (выход из программы) или брошен, потому что завис на прошлой задаче.</summary>
public sealed class StaWorkerStoppedException() : Exception("Поток обработки остановлен");

/// <summary>
/// Отдельный поток со своей очередью — для COM-объектов Office: они живут в одном потоке (STA)
/// и вызываются строго по очереди, по одной задаче.
/// <para>
/// На Windows поток STA. Пока очередь пуста, CLR ждёт в STA «с прокачкой сообщений» (CoWaitForMultipleHandles),
/// так что Office, рассылающий сообщения всем окнам (DDE при запуске), на нас не зависает.
/// Очередь своя, а не оконные сообщения (как у Dispatcher): во время долгого вызова Office COM крутит
/// цикл сообщений, и следующая задача не может «влезть» внутрь текущей.
/// </para>
/// <para>
/// RunAsync — задача со сторожем: таймер идёт с момента, когда задача реально началась (до этого она
/// могла ждать в очереди за другими документами). Таймаут или отмена → abort() (убить процесс Office),
/// и если задача не вернулась и после этого — поток считается зависшим (IsStuck) и бросается.
/// </para>
/// </summary>
public sealed class StaWorker
{
    private sealed record Item(Action Run, Action<Exception> Fail);

    /// <summary>Состояние задачи RunAsync: ждёт в очереди → началась или снята (отменили, пока ждала).</summary>
    private sealed class Gate { public int State; }
    private const int Queued = 0, Started = 1, Dropped = 2;

    private readonly BlockingCollection<Item> _queue = new();
    private readonly Thread _thread;
    private readonly Action<string>? _log;
    private volatile bool _stuck;

    /// <param name="onStart">Выполняется в потоке до первой задачи (регистрация фильтра сообщений COM).</param>
    /// <param name="onStop">Выполняется в потоке после последней задачи (закрыть Office).</param>
    public StaWorker(string name, Action? onStart = null, Action? onStop = null, Action<string>? log = null)
    {
        _log = log;
        _thread = new Thread(() => Loop(onStart, onStop)) { IsBackground = true, Name = name };
        if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Сколько ждать, пока прерванная задача вернёт управление; дольше — поток зависший.</summary>
    public TimeSpan AbortGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Задача не вернулась даже после abort: поток брошен, новые задачи не принимаются.</summary>
    public bool IsStuck => _stuck;

    /// <summary>Новые задачи не принимаются (Complete или поток брошен).</summary>
    public bool IsStopped => _queue.IsAddingCompleted;

    public Task InvokeAsync(Action action) => InvokeAsync<object?>(() =>
    {
        action();
        return null;
    });

    /// <summary>Выполнить в потоке и вернуть результат. Исключение функции — в задаче, поток продолжает работу.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new Item(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (OperationCanceledException ex) { tcs.TrySetCanceled(ex.CancellationToken); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }, ex => tcs.TrySetException(ex));
        if (!TryAdd(item)) tcs.TrySetException(new StaWorkerStoppedException());
        return tcs.Task;
    }

    /// <summary>
    /// Выполнить работу в потоке не дольше timeout с момента её начала.
    /// Отмена, пока задача в очереди, — задача снимается и не выполняется.
    /// Таймаут или отмена во время работы — токен работы отменяется, вызывается abort (из другого потока),
    /// затем ждём завершения ещё AbortGrace; не дождались — поток брошен (IsStuck), очередь отклонена.
    /// </summary>
    /// <exception cref="OperationCanceledException">Отменили (ct).</exception>
    /// <exception cref="TimeoutException">Работа шла дольше timeout.</exception>
    /// <exception cref="StaWorkerStoppedException">Поток остановлен или брошен до начала задачи.</exception>
    public async Task RunAsync(Action<CancellationToken> work, TimeSpan timeout, Action abort, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gate = new Gate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = stop.Token;
        var done = InvokeAsync(() =>
        {
            if (Interlocked.CompareExchange(ref gate.State, Started, Queued) != Queued) return; // сняли, пока ждала
            started.TrySetResult();
            work(token);
        });

        // 1. Ждём начала: пока задача в очереди, таймер не идёт.
        try
        {
            await Task.WhenAny(started.Task, done).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (Interlocked.CompareExchange(ref gate.State, Dropped, Queued) == Queued) throw;
            await Task.WhenAny(started.Task, done).ConfigureAwait(false); // успела начаться — дальше как обычно
        }

        // 2. Работает (или уже закончилась): ждём, но не дольше timeout.
        try
        {
            await done.WaitAsync(timeout, ct).ConfigureAwait(false);
            return;
        }
        catch (Exception) when (done.IsCompleted)
        {
            await done.ConfigureAwait(false); // ошибка самой работы (или она успела к сроку)
            return;
        }
        catch (Exception)
        {
            // таймаут или отмена во время работы
        }

        // 3. Останавливаем: токен работы, затем abort — убить процесс, на котором она висит.
        try { stop.Cancel(); }
        catch (Exception ex) { _log?.Invoke("Поток: отмена задачи — " + ex.Message); }
        try { abort(); }
        catch (Exception ex) { _log?.Invoke("Поток: прерывание задачи — " + ex.Message); }

        try
        {
            await done.WaitAsync(AbortGrace).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!done.IsCompleted)
        {
            Abandon();
        }
        catch (Exception)
        {
            // работа упала после abort — так и задумано
        }
        Observe(done);

        ct.ThrowIfCancellationRequested();
        throw new TimeoutException($"Задача не завершилась за {timeout.TotalSeconds:0} с");
    }

    /// <summary>Больше не принимать задачи: выполнить уже поставленные, затем onStop, и поток завершится.</summary>
    public void Complete()
    {
        try { _queue.CompleteAdding(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Дождаться завершения потока (после Complete).</summary>
    public bool Join(TimeSpan timeout) => _thread.Join(timeout);

    /// <summary>
    /// Поток завис внутри задачи: больше ничего в него не ставим, ожидающие задачи отклоняем сразу.
    /// Сам поток закончит висящую задачу, когда (если) она вернётся, выполнит onStop и выйдет.
    /// </summary>
    private void Abandon()
    {
        _stuck = true;
        _log?.Invoke($"Поток «{_thread.Name}» не отвечает — брошен");
        Complete();
        while (_queue.TryTake(out var item))
            item.Fail(new StaWorkerStoppedException());
    }

    private bool TryAdd(Item item)
    {
        try { return _queue.TryAdd(item); }
        catch (InvalidOperationException) { return false; } // CompleteAdding уже был
    }

    private void Loop(Action? onStart, Action? onStop)
    {
        Safe(onStart, "запуск");
        foreach (var item in _queue.GetConsumingEnumerable())
            Safe(item.Run, "задача");
        Safe(onStop, "остановка");
    }

    /// <summary>Необработанное исключение в своём потоке уронило бы всю программу — ловим всё.</summary>
    private void Safe(Action? action, string what)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { _log?.Invoke($"Поток «{_thread.Name}», {what}: {ex}"); }
    }

    private static void Observe(Task task) =>
        task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
