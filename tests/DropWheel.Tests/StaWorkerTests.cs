using DropWheel.Engines;

namespace DropWheel.Tests;

/// <summary>Поток Office: очередь, сторож (таймаут/отмена/зависание). COM здесь не нужен — проверяется логика.</summary>
public class StaWorkerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(10);
    private readonly List<string> _log = [];

    private StaWorker NewWorker(Action? onStart = null, Action? onStop = null) =>
        new("тест", onStart, onStop, m => { lock (_log) _log.Add(m); }) { AbortGrace = TimeSpan.FromMilliseconds(500) };

    [Fact]
    public async Task Runs_items_in_order_on_one_dedicated_thread()
    {
        var worker = NewWorker();
        var order = new List<int>();
        var tasks = Enumerable.Range(0, 20).Select(i => worker.InvokeAsync(() =>
        {
            order.Add(i);
            return (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState());
        })).ToList();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results.Select(r => r.Item1).Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, results[0].Item1);
        Assert.Equal(Enumerable.Range(0, 20), order);
        if (OperatingSystem.IsWindows()) Assert.Equal(ApartmentState.STA, results[0].Item2);

        worker.Complete();
        Assert.True(worker.Join(Long));
    }

    [Fact]
    public async Task Error_goes_to_caller_and_worker_goes_on()
    {
        var worker = NewWorker();
        await Assert.ThrowsAsync<ConversionException>(() => worker.InvokeAsync(() => throw new ConversionException("плохо")));
        await Assert.ThrowsAsync<ConversionException>(() =>
            worker.RunAsync(_ => throw new ConversionException("плохо"), Long, () => { }, default));
        Assert.Equal(42, await worker.InvokeAsync(() => 42));
    }

    [Fact]
    public async Task Timeout_aborts_and_worker_goes_on()
    {
        var worker = NewWorker();
        using var aborted = new ManualResetEventSlim();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            worker.RunAsync(_ => aborted.Wait(Long), TimeSpan.FromMilliseconds(100), aborted.Set, default));

        Assert.True(aborted.IsSet);
        Assert.False(worker.IsStuck);
        Assert.Equal(42, await worker.InvokeAsync(() => 42));
    }

    [Fact]
    public async Task Work_token_is_cancelled_on_timeout()
    {
        var worker = NewWorker();
        var sawCancel = false;
        await Assert.ThrowsAsync<TimeoutException>(() => worker.RunAsync(token =>
        {
            token.WaitHandle.WaitOne(Long);
            sawCancel = token.IsCancellationRequested;
        }, TimeSpan.FromMilliseconds(100), () => { }, default));
        Assert.True(sawCancel);
        Assert.False(worker.IsStuck);
    }

    [Fact]
    public async Task Timer_starts_when_work_starts_not_when_queued()
    {
        var worker = NewWorker();
        var first = worker.RunAsync(_ => Thread.Sleep(600), Long, () => { }, default);
        // Ждёт в очереди дольше своего таймаута, но сама работа быстрая — таймаута нет.
        var second = worker.RunAsync(_ => Thread.Sleep(10), TimeSpan.FromMilliseconds(300), () => { }, default);
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Cancel_while_queued_skips_the_work()
    {
        var worker = NewWorker();
        using var gate = new ManualResetEventSlim();
        var blocker = worker.InvokeAsync(() => gate.Wait(Long));
        var ran = false;
        var aborted = false;
        using var cts = new CancellationTokenSource();
        var queued = worker.RunAsync(_ => ran = true, Long, () => aborted = true, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        gate.Set();
        await blocker;
        await worker.InvokeAsync(() => { }); // очередь прошла

        Assert.False(ran);
        Assert.False(aborted);
    }

    [Fact]
    public async Task Cancel_while_running_aborts()
    {
        var worker = NewWorker();
        using var started = new ManualResetEventSlim();
        using var aborted = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        var task = worker.RunAsync(_ =>
        {
            started.Set();
            aborted.Wait(Long);
        }, Long, aborted.Set, cts.Token);

        Assert.True(started.Wait(Long));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(aborted.IsSet);
        Assert.False(worker.IsStuck);
    }

    [Fact]
    public async Task Hung_work_marks_worker_stuck_and_rejects_the_queue()
    {
        using var release = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var worker = NewWorker(onStop: stopped.Set);

        var hung = worker.RunAsync(_ => release.Wait(Long), TimeSpan.FromMilliseconds(100), () => { }, default);
        var queued = worker.InvokeAsync(() => 1);

        await Assert.ThrowsAsync<TimeoutException>(() => hung);
        Assert.True(worker.IsStuck);
        await Assert.ThrowsAsync<StaWorkerStoppedException>(() => queued);
        await Assert.ThrowsAsync<StaWorkerStoppedException>(() => worker.InvokeAsync(() => 2));
        await Assert.ThrowsAsync<StaWorkerStoppedException>(() => worker.RunAsync(_ => { }, Long, () => { }, default));

        // Зависшая задача вернулась — поток доделывает, выполняет onStop (закрыть Office) и выходит.
        release.Set();
        Assert.True(stopped.Wait(Long));
        Assert.True(worker.Join(Long));
    }

    [Fact]
    public async Task Complete_runs_the_queue_then_on_stop()
    {
        var steps = new List<string>();
        var worker = NewWorker(onStart: () => steps.Add("старт"), onStop: () => steps.Add("стоп"));
        _ = worker.InvokeAsync(() =>
        {
            Thread.Sleep(50);
            steps.Add("1");
        });
        _ = worker.InvokeAsync(() => steps.Add("2"));
        worker.Complete();

        Assert.True(worker.Join(Long));
        Assert.Equal(["старт", "1", "2", "стоп"], steps);
        await Assert.ThrowsAsync<StaWorkerStoppedException>(() => worker.InvokeAsync(() => 3));
    }

    [Fact]
    public async Task Failing_callbacks_do_not_kill_the_thread()
    {
        var worker = NewWorker(onStart: () => throw new InvalidOperationException("сбой запуска"));
        Assert.Equal(1, await worker.InvokeAsync(() => 1));
        lock (_log) Assert.Contains(_log, m => m.Contains("сбой запуска"));
    }
}
