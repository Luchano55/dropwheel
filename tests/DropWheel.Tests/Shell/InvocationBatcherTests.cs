using System.Collections.Concurrent;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class InvocationBatcherTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public void Burst_of_adds_gives_one_deduped_ordered_flush()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        using var batcher = new InvocationBatcher(Quiet, flushes.Add);

        batcher.Add([@"C:\Мои фото\б.jpg"]);
        batcher.Add([@"C:\Мои фото\а.jpg", @"C:\Мои фото\б.jpg"]);
        batcher.Add([]);
        batcher.Add([@"C:\Мои фото\в 1.jpg", @"C:\Мои фото\а.jpg"]);

        Assert.True(flushes.TryTake(out var batch, Wait));
        Assert.Equal([@"C:\Мои фото\б.jpg", @"C:\Мои фото\а.jpg", @"C:\Мои фото\в 1.jpg"], batch);
        // Второй пачки нет.
        Assert.False(flushes.TryTake(out _, Quiet * 5));
    }

    [Fact]
    public void Timer_restarts_on_each_add()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        using var batcher = new InvocationBatcher(TimeSpan.FromMilliseconds(300), flushes.Add);

        // Пути приходят чаще, чем раз в 300 мс, — суммарно дольше 300 мс, но пачка одна.
        for (var i = 0; i < 6; i++)
        {
            batcher.Add([$"файл {i}"]);
            Thread.Sleep(100);
        }

        Assert.True(flushes.TryTake(out var batch, Wait));
        Assert.Equal(6, batch.Count);
    }

    [Fact]
    public void Two_separated_bursts_give_two_flushes()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        using var batcher = new InvocationBatcher(Quiet, flushes.Add);

        batcher.Add(["раз.mp4", "два.mp4"]);
        Assert.True(flushes.TryTake(out var first, Wait));

        // После отправки пачки тот же путь снова считается новым.
        batcher.Add(["два.mp4", "три.mp4"]);
        Assert.True(flushes.TryTake(out var second, Wait));

        Assert.Equal(["раз.mp4", "два.mp4"], first);
        Assert.Equal(["два.mp4", "три.mp4"], second);
    }

    [Fact]
    public void Empty_add_does_not_flush()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        using var batcher = new InvocationBatcher(Quiet, flushes.Add);

        batcher.Add([]);
        Assert.False(flushes.TryTake(out _, Quiet * 5));
    }

    [Fact]
    public void Dispose_drops_pending_batch()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        var batcher = new InvocationBatcher(Quiet, flushes.Add);
        batcher.Add(["потеряется.jpg"]);
        batcher.Dispose();
        batcher.Add(["после.jpg"]);

        Assert.False(flushes.TryTake(out _, Quiet * 5));
    }

    [Fact]
    public void Dedupe_respects_platform_case_rules()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        using var batcher = new InvocationBatcher(Quiet, flushes.Add);

        batcher.Add(["Фото.JPG", "фото.jpg"]);

        Assert.True(flushes.TryTake(out var batch, Wait));
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, batch.Count);
        Assert.Equal("Фото.JPG", batch[0]);
    }

    [Fact]
    public void Throwing_handler_does_not_break_next_batch()
    {
        var flushes = new BlockingCollection<IReadOnlyList<string>>();
        var calls = 0;
        using var batcher = new InvocationBatcher(Quiet, b =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException();
            flushes.Add(b);
        });

        batcher.Add(["первый"]);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, Wait));
        batcher.Add(["второй"]);

        Assert.True(flushes.TryTake(out var batch, Wait));
        Assert.Equal(["второй"], batch);
    }
}
