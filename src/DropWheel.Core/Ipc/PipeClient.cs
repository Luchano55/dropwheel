using System.IO.Pipes;

namespace DropWheel.Ipc;

/// <summary>Второй экземпляр отправляет свои аргументы первому и завершается.</summary>
public static class PipeClient
{
    /// <summary>
    /// true — сервер принял сообщение (прислал подтверждение). false — сервера нет,
    /// истекло время или любая другая ошибка. Не бросает.
    /// </summary>
    public static bool TrySend(string pipeName, string[] args, int timeoutMs = 2000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.ConnectAsync(cts.Token).GetAwaiter().GetResult();

            var message = PipeProtocol.Encode(args);
            pipe.WriteAsync(message, cts.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(cts.Token).GetAwaiter().GetResult();

            var answer = new byte[1];
            var read = pipe.ReadAsync(answer, cts.Token).AsTask().GetAwaiter().GetResult();
            return read == 1 && answer[0] == PipeProtocol.Ack;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
