using DropWheel.Ipc;

namespace DropWheel.Tests.Ipc;

public class SingleInstanceTests
{
    private static string NewName() => "DropWheel-test-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void Second_acquire_fails_until_first_is_disposed()
    {
        var name = NewName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        Assert.Null(SingleInstance.TryAcquire(name));

        first.Dispose();
        using var again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Dispose_from_another_thread_releases()
    {
        var name = NewName();
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);

        await Task.Run(first.Dispose);
        first.Dispose(); // повторный Dispose безопасен

        using var again = SingleInstance.TryAcquire(name);
        Assert.NotNull(again);
    }

    [Fact]
    public void Different_names_do_not_conflict()
    {
        using var a = SingleInstance.TryAcquire(NewName());
        using var b = SingleInstance.TryAcquire(NewName());
        Assert.NotNull(a);
        Assert.NotNull(b);
    }
}
