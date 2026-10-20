using System.Diagnostics;

namespace DropWheel.Shell;

/// <summary>
/// Проводник запускает отдельный процесс на каждый выделенный файл. Первый экземпляр собирает
/// пути и отдаёт их одной пачкой, когда новые перестали приходить на время quiet (решение D6).
/// </summary>
public sealed class InvocationBatcher : IDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly TimeSpan _quiet;
    private readonly Action<IReadOnlyList<string>> _flushed;
    private readonly object _lock = new();
    // Отдельный замок для вызова flushed: пачки уходят по очереди, но Add при этом не ждёт.
    private readonly object _flushLock = new();
    private readonly Timer _timer;
    private readonly List<string> _paths = [];
    private readonly HashSet<string> _seen = new(PathComparer);
    private long _lastAdd;
    private bool _disposed;

    /// <summary>flushed вызывается в фоновом потоке.</summary>
    public InvocationBatcher(TimeSpan quiet, Action<IReadOnlyList<string>> flushed)
    {
        _quiet = quiet;
        _flushed = flushed;
        _timer = new Timer(_ => OnTimer());
    }

    /// <summary>Добавляет пути (без повторов, в порядке прихода) и перезапускает отсчёт тишины.</summary>
    public void Add(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var added = false;
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p) || !_seen.Add(p)) continue;
                _paths.Add(p);
                added = true;
            }
            if (!added) return;
            _lastAdd = Stopwatch.GetTimestamp();
            _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer()
    {
        List<string> batch;
        lock (_lock)
        {
            if (_disposed || _paths.Count == 0) return;
            // Таймер мог сработать одновременно с Add — тогда досчитываем оставшуюся тишину.
            var left = _quiet - Stopwatch.GetElapsedTime(_lastAdd);
            if (left > TimeSpan.Zero)
            {
                _timer.Change(left, Timeout.InfiniteTimeSpan);
                return;
            }
            batch = [.. _paths];
            _paths.Clear();
            _seen.Clear();
        }

        lock (_flushLock)
        {
            try { _flushed(batch); }
            catch (Exception) { }
        }
    }

    /// <summary>Несобранная пачка выбрасывается.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _paths.Clear();
            _seen.Clear();
            _timer.Dispose();
        }
    }
}
