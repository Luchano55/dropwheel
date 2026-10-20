using System.IO.Pipes;

namespace DropWheel.Ipc;

/// <summary>
/// Первый экземпляр слушает named pipe: второй присылает ему свои аргументы командной строки.
/// Одно сообщение на подключение; клиенты обслуживаются параллельно, а onMessage вызывается
/// по очереди (не одновременно) в фоновом потоке.
/// </summary>
public sealed class PipeServer(string pipeName, Action<string[]> onMessage) : IDisposable
{
    /// <summary>Сколько ждём сообщения от подключившегося клиента — зависший клиент не держит сервер.</summary>
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cts = new();
    private readonly object _dispatch = new();
    private Task? _loop;

    public void Start()
    {
        if (_cts.IsCancellationRequested) throw new ObjectDisposedException(nameof(PipeServer));
        _loop ??= Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (Exception)
            {
                // Не удалось создать pipe или клиент оборвал подключение — пробуем снова.
                pipe?.Dispose();
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            var connected = pipe;
            _ = Task.Run(() => HandleAsync(connected, ct));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        string[]? args = null;
        try
        {
            using (pipe)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(ClientTimeout);
                args = await PipeProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
                if (args is null) return;

                await pipe.WriteAsync(new[] { PipeProtocol.Ack }, timeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
                // Ждём, пока клиент прочитает ответ и закроет соединение: если закрыть pipe раньше,
                // Windows может выбросить непрочитанный байт.
                await pipe.ReadAsync(new byte[1], timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Битый, медленный или оборвавшийся клиент — просто забываем о нём.
        }

        if (args is null || ct.IsCancellationRequested) return;
        try
        {
            lock (_dispatch) onMessage(args);
        }
        catch (Exception)
        {
            // Ошибка обработчика не должна останавливать сервер.
        }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
    }
}
