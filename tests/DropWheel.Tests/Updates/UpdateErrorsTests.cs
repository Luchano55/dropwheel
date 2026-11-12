using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using DropWheel.Localization;
using DropWheel.Updates;

namespace DropWheel.Tests.Updates;

/// <summary>Понятные сообщения, когда проверка обновлений упала из-за сети.</summary>
public class UpdateErrorsTests
{
    private static HttpRequestException Http(Exception inner) => new("Требуемый адрес для своего контекста неверен. (github.com:443)", inner);

    [Theory]
    [InlineData(SocketError.AddressNotAvailable)] // github.com подменён на 0.0.0.0 (hosts, антивирус, DNS)
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.ConnectionRefused)]
    public void Blocked_github_is_explained(SocketError code)
    {
        Assert.Equal(Loc.T.UpdateErrBlocked, UpdateErrors.Explain(Http(new SocketException((int)code))));
    }

    [Fact]
    public void Offline_timeout_tls_and_rate_limit()
    {
        Assert.Equal(Loc.T.UpdateErrOffline, UpdateErrors.Explain(Http(new SocketException((int)SocketError.NetworkUnreachable))));
        Assert.Equal(Loc.T.UpdateErrTimeout, UpdateErrors.Explain(Http(new SocketException((int)SocketError.TimedOut))));
        Assert.Equal(Loc.T.UpdateErrTimeout, UpdateErrors.Explain(new TaskCanceledException("timeout", new TimeoutException())));
        Assert.Equal(Loc.T.UpdateErrSecure, UpdateErrors.Explain(Http(new AuthenticationException("bad cert"))));
        Assert.Equal(Loc.T.UpdateErrBusy, UpdateErrors.Explain(new HttpRequestException("403", null, HttpStatusCode.Forbidden)));
    }

    [Fact]
    public void Other_errors_keep_their_message()
    {
        Assert.Equal("Контрольная сумма не совпала", UpdateErrors.Explain(new InvalidOperationException("Контрольная сумма не совпала")));
    }
}
