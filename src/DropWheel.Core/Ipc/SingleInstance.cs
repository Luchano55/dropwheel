namespace DropWheel.Ipc;

/// <summary>
/// Один запущенный экземпляр на пользователя — через именованный mutex.
/// Mutex принадлежит потоку, поэтому им владеет отдельный фоновый поток: так Dispose можно
/// вызывать из любого потока, а повторный захват в том же процессе честно возвращает null.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly ManualResetEventSlim _release;
    private readonly Thread _owner;
    private int _disposed;

    private SingleInstance(ManualResetEventSlim release, Thread owner)
    {
        _release = release;
        _owner = owner;
    }

    /// <summary>null — mutex уже держит другой экземпляр.</summary>
    public static SingleInstance? TryAcquire(string name)
    {
        var acquired = new TaskCompletionSource<bool>();
        var release = new ManualResetEventSlim();
        var owner = new Thread(() => Own(name, acquired, release))
        {
            IsBackground = true,
            Name = "DropWheel.SingleInstance",
        };
        owner.Start();

        if (acquired.Task.GetAwaiter().GetResult()) return new SingleInstance(release, owner);
        owner.Join();
        release.Dispose();
        return null;
    }

    private static void Own(string name, TaskCompletionSource<bool> acquired, ManualResetEventSlim release)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
            bool owned;
            try { owned = mutex.WaitOne(0); }
            // Прошлый экземпляр упал, не отпустив mutex, — теперь он наш.
            catch (AbandonedMutexException) { owned = true; }

            acquired.SetResult(owned);
            if (!owned) return;
            release.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            // Mutex с таким именем создан кем-то другим (другие права/тип) — считаем, что занято.
            acquired.TrySetResult(false);
        }
        catch (Exception ex)
        {
            acquired.TrySetException(ex);
        }
        finally
        {
            mutex?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release.Set();
        _owner.Join(TimeSpan.FromSeconds(2));
    }
}
