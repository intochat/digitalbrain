using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DigitalBrain.Integrations.Mcp;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class McpOAuthCallbacks
{
    [Fact]
    public async Task StateValidDenialReturnsTerminalResponseAndCompletes()
    {
        var redirectUri = RedirectUri();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var launch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorization = Authorize(redirectUri, () => launch.SetResult(), cancellation.Token);
        await launch.Task.WaitAsync(cancellation.Token);

        using var response = await SendAsync(redirectUri, "?state=expected&error=access_denied", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DigitalBrain authorization was denied. You can close this window.", await response.Content.ReadAsStringAsync(cancellation.Token));
        Assert.Null(await authorization.WaitAsync(cancellation.Token));
        AssertPortReleased(redirectUri);
    }

    [Fact]
    public async Task InvalidCallbacksDoNotTerminateFlowButValidCodeCompletes()
    {
        var redirectUri = RedirectUri();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var launch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authorization = Authorize(redirectUri, () => launch.SetResult(), cancellation.Token);
        await launch.Task.WaitAsync(cancellation.Token);

        using var wrongState = await SendAsync(redirectUri, "?state=foreign&code=nope", cancellation.Token);
        using var wrongPath = await SendAsync(new Uri(redirectUri, "/other?state=expected&code=nope"), string.Empty, cancellation.Token);
        using var malformed = await SendAsync(redirectUri, "?state=expected", cancellation.Token);

        Assert.Equal(HttpStatusCode.BadRequest, wrongState.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongPath.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        await AssertIncompleteAsync(authorization);

        using var completed = await SendAsync(redirectUri, "?state=expected&code=accepted", cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("accepted", await authorization.WaitAsync(cancellation.Token));
        AssertPortReleased(redirectUri);
    }

    [Fact]
    public void MissingCallbackUriIsRejected()
    {
        var validate = typeof(LocalLoopbackMcpAuthorizationRedirect).GetMethod(
            "ValidateCallback",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var exception = Assert.Throws<TargetInvocationException>(() => validate.Invoke(
            null,
            [new Uri("https://provider.example/authorize?state=expected"), RedirectUri(), null]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    private static Task<string?> Authorize(
        Uri redirectUri,
        Action launched,
        CancellationToken cancellationToken)
    {
        var core = typeof(LocalLoopbackMcpAuthorizationRedirect).GetMethod(
            "AuthorizeAsyncCore",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var start = (Action<ProcessStartInfo>)(_ => launched());
        return (Task<string?>)core.Invoke(
            null,
            [new Uri("https://provider.example/authorize?state=expected"), redirectUri, start, cancellationToken])!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        string query,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        return await client.GetAsync(new Uri(uri + query), cancellationToken);
    }

    private static async Task AssertIncompleteAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(task, completed);
    }

    private static Uri RedirectUri()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}/callback");
    }

    private static void AssertPortReleased(Uri redirectUri)
    {
        using var listener = new TcpListener(IPAddress.Loopback, redirectUri.Port);
        listener.Start();
    }
}
