using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Jobs;

public enum JobState { Queued, Running, Done, Failed, Canceled }

public sealed class Job
{
    private readonly CancellationTokenSource _cts = new();

    internal Job(string input, Operation operation)
    {
        Input = input;
        Operation = operation;
    }

    public string Input { get; }
    public Operation Operation { get; }
    public JobState State { get; internal set; }
    /// <summary>0..1; для ImageMagick прогресса нет — скачок с 0 до 1.</summary>
    public double Progress { get; internal set; }
    public string? OutputPath { get; internal set; }
    public string? Error { get; internal set; }
    public string? ErrorDetails { get; internal set; }
    public Task Completion => _done.Task;

    internal readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken Token => _cts.Token;

    public void Cancel() => _cts.Cancel();
}

/// <summary>Фоновая очередь: не больше MaxParallel конвертаций одновременно.</summary>
public sealed class JobQueue(Converter converter, int maxParallel = 2, Action<string>? log = null)
{
    private readonly SemaphoreSlim _slots = new(maxParallel, maxParallel);
    private readonly List<Job> _jobs = [];

    /// <summary>Любое изменение задачи: состояние или прогресс. Вызывается из фонового потока.</summary>
    public event Action<Job>? Changed;

    public IReadOnlyList<Job> Jobs { get { lock (_jobs) return _jobs.ToList(); } }

    public Job Enqueue(string input, Operation op)
    {
        var job = new Job(input, op);
        lock (_jobs) _jobs.Add(job);
        Changed?.Invoke(job);
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    private async Task RunAsync(Job job)
    {
        try
        {
            await _slots.WaitAsync(job.Token);
            try
            {
                Set(job, JobState.Running);
                var progress = new Progress(p => { job.Progress = p; Changed?.Invoke(job); });
                job.OutputPath = await converter.ConvertAsync(job.Input, job.Operation, progress, log, job.Token);
                job.Progress = 1;
                Set(job, JobState.Done);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Set(job, JobState.Canceled);
        }
        catch (ConversionException ex)
        {
            job.Error = ex.Message;
            job.ErrorDetails = ex.Details;
            Set(job, JobState.Failed);
        }
        catch (Exception ex)
        {
            job.Error = "Непредвиденная ошибка";
            job.ErrorDetails = ex.ToString();
            Set(job, JobState.Failed);
        }
        finally
        {
            job._done.TrySetResult();
        }
    }

    private void Set(Job job, JobState state)
    {
        job.State = state;
        log?.Invoke($"[{state}] {job.Operation.Id} {job.Input}{(job.Error is null ? "" : " — " + job.Error)}");
        Changed?.Invoke(job);
    }

    /// <summary>IProgress без SynchronizationContext — вызывается прямо в потоке утилиты.</summary>
    private sealed class Progress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
