using System.Collections.Concurrent;
using System.IO.Pipes;
using DropWheel.Ipc;

namespace DropWheel.Tests.Ipc;

public class PipeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>Уникальное имя на тест — тесты идут параллельно.</summary>
    private static string NewPipeName() => "DropWheel-test-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public void Pipe_name_is_stable_and_safe()
    {
        var a = PipeNames.ForCurrentUser("DropWheel");
        Assert.Equal(a, PipeNames.ForCurrentUser("DropWheel"));
        Assert.StartsWith("DropWheel-", a);
        Assert.Matches("^[A-Za-z0-9._-]+$", a);
        Assert.NotEqual(a, PipeNames.ForCurrentUser("DropWheel.Pipe"));
        Assert.Matches("^[A-Za-z0-9._-]+$", PipeNames.ForCurrentUser(@"Мой\канал"));
    }

    [Fact]
    public void Round_trip_with_cyrillic_spaces_and_quotes()
    {
        var name = NewPipeName();
        var got = new BlockingCollection<string[]>();
        using var server = new PipeServer(name, got.Add);
        server.Start();

        string[] args = ["--convert", @"C:\Мои фото\тест 1.heic", "кавычки \" внутри", "'одинарные'", "", "emoji 🎞"];
        Assert.True(PipeClient.TrySend(name, args, 5000));

        Assert.True(got.TryTake(out var received, Wait));
        Assert.Equal(args, received);
    }

    [Fact]
    public void Thousand_paths_arrive_intact()
    {
        var name = NewPipeName();
        var got = new BlockingCollection<string[]>();
        using var server = new PipeServer(name, got.Add);
        server.Start();

        var args = Enumerable.Range(0, 1000)
            .Select(i => $@"C:\Users\Роман\Рабочий стол\Папка с длинным именем {i}\видео ролик номер {i}.mp4")
            .ToArray();
        Assert.True(PipeClient.TrySend(name, args, 5000));

        Assert.True(got.TryTake(out var received, Wait));
        Assert.Equal(args, received);
    }

    [Fact]
    public void Several_clients_one_after_another()
    {
        var name = NewPipeName();
        var got = new BlockingCollection<string[]>();
        using var server = new PipeServer(name, got.Add);
        server.Start();

        for (var i = 0; i < 5; i++)
            Assert.True(PipeClient.TrySend(name, [$"файл {i}.jpg"], 5000));

        var all = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(got.TryTake(out var m, Wait));
            all.AddRange(m);
        }
        Assert.Equal(Enumerable.Range(0, 5).Select(i => $"файл {i}.jpg").Order(), all.Order());
    }

    [Fact]
    public void Parallel_clients_all_delivered()
    {
        var name = NewPipeName();
        var got = new ConcurrentBag<string>();
        using var server = new PipeServer(name, m => { foreach (var a in m) got.Add(a); });
        server.Start();

        var results = new bool[10];
        Parallel.For(0, 10, i => results[i] = PipeClient.TrySend(name, [$"файл {i}"], 5000));

        Assert.All(results, Assert.True);
        Assert.True(SpinWait.SpinUntil(() => got.Count == 10, Wait));
    }

    [Fact]
    public void No_server_returns_false()
    {
        Assert.False(PipeClient.TrySend(NewPipeName(), ["x"], 300));
    }

    [Fact]
    public void Disposed_server_stops_answering()
    {
        var name = NewPipeName();
        var server = new PipeServer(name, _ => { });
        server.Start();
        Assert.True(PipeClient.TrySend(name, ["раз"], 5000));
        server.Dispose();
        Assert.False(PipeClient.TrySend(name, ["два"], 300));
    }

    [Fact]
    public void Server_survives_garbage_clients_and_throwing_handler()
    {
        var name = NewPipeName();
        var got = new BlockingCollection<string[]>();
        using var server = new PipeServer(name, m =>
        {
            if (m is ["бросить"]) throw new InvalidOperationException("сбой обработчика");
            got.Add(m);
        });
        server.Start();

        // Мусор: огромная длина, битый JSON, не-массив, обрыв посреди сообщения.
        SendRaw(name, [0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3]);
        SendRaw(name, [5, 0, 0, 0, (byte)'{', (byte)'x', (byte)'"', (byte)'[', (byte)']']);
        SendRaw(name, [2, 0, 0, 0, (byte)'4', (byte)'2']);
        SendRaw(name, [100, 0, 0, 0, (byte)'[']);
        SendRaw(name, []);
        Assert.True(PipeClient.TrySend(name, ["бросить"], 5000));

        Assert.True(PipeClient.TrySend(name, ["после мусора"], 5000));
        Assert.True(got.TryTake(out var m, Wait));
        Assert.Equal(["после мусора"], m);
    }

    private static void SendRaw(string name, byte[] bytes)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        pipe.Connect(5000);
        pipe.Write(bytes);
        pipe.Flush();
    }
}
