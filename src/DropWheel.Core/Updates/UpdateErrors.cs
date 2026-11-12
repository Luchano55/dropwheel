using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using DropWheel.Localization;

namespace DropWheel.Updates;

/// <summary>
/// Понятный текст вместо системного сообщения, когда проверка или загрузка обновления упала из-за сети.
/// Например, «Требуемый адрес для своего контекста неверен (github.com:443)» — github.com подменён на 0.0.0.0
/// (файл hosts, антивирус, DNS провайдера): пользователю это ничего не говорит.
/// </summary>
public static class UpdateErrors
{
    public static string Explain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case SocketException s when s.SocketErrorCode is SocketError.AddressNotAvailable or SocketError.HostNotFound
                    or SocketError.ConnectionRefused or SocketError.AccessDenied:
                    return Loc.T.UpdateErrBlocked;
                case SocketException s when s.SocketErrorCode is SocketError.NetworkUnreachable or SocketError.NetworkDown
                    or SocketError.HostUnreachable or SocketError.TryAgain or SocketError.NoData:
                    return Loc.T.UpdateErrOffline;
                case SocketException s when s.SocketErrorCode is SocketError.TimedOut:
                    return Loc.T.UpdateErrTimeout;
                case AuthenticationException:
                    return Loc.T.UpdateErrSecure;
                case TimeoutException:
                    return Loc.T.UpdateErrTimeout;
                case HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }:
                    return Loc.T.UpdateErrBusy;
            }
        }
        // HttpClient по таймауту бросает TaskCanceledException без нашей отмены.
        if (error is TaskCanceledException) return Loc.T.UpdateErrTimeout;
        return error.Message;
    }
}
