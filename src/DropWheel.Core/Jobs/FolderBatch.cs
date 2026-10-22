namespace DropWheel.Jobs;

/// <summary>
/// Задачи одной «папки целиком» (D-FOLDERS): вместо уведомления на каждый из 300 файлов — одно итоговое,
/// когда закончились все. Вызывать из одного потока (потока интерфейса).
/// </summary>
public sealed class FolderBatch(IReadOnlyList<Job> jobs, IReadOnlyList<string> outputFolders, int skipped)
{
    private readonly HashSet<Job> _jobs = [.. jobs];
    private readonly HashSet<Job> _finished = [];
    private readonly List<(string Input, string Reason)> _failures = [];

    /// <summary>Созданные папки-результаты («Отпуск (JPG)»).</summary>
    public IReadOnlyList<string> OutputFolders { get; } = outputFolders;
    public int Total => _jobs.Count;
    /// <summary>Файлов, к которым операция не подходит.</summary>
    public int Skipped { get; } = skipped;
    public int Done { get; private set; }
    public int Canceled { get; private set; }
    public IReadOnlyList<(string Input, string Reason)> Failures => _failures;
    public bool Contains(Job job) => _jobs.Contains(job);

    public bool IsComplete => _finished.Count == _jobs.Count;

    /// <summary>
    /// Учесть задачу в её текущем состоянии. true — задача только что учтена как законченная
    /// (повторные события той же задачи, чужие задачи и незавершённые состояния — false).
    /// Пачка закончилась, когда после учёта <see cref="IsComplete"/>.
    /// </summary>
    public bool Record(Job job, string unknownError = "")
    {
        if (!_jobs.Contains(job) || job.State is JobState.Queued or JobState.Running || !_finished.Add(job)) return false;
        switch (job.State)
        {
            case JobState.Done:
                Done++;
                break;
            case JobState.Failed:
                _failures.Add((job.Input, job.Error ?? unknownError));
                break;
            default:
                Canceled++;
                break;
        }
        return true;
    }
}
