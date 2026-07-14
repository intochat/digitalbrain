extern alias McpProject;

using System.Net;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuthorizationFlowProxyOptions = McpProject::DigitalBrain.Mcp.AuthorizationFlowProxyOptions;
using AuthorizationFlowStartProxy = McpProject::DigitalBrain.Mcp.AuthorizationFlowStartProxy;

namespace DigitalBrain.Tests.Runtime;

public sealed class AuthorizationFlowStartProxyTests
{
    private const string FlowReference = "abcdefghijklmnopqrstuvwxyzABCDEF0123456789-_";

    [Fact]
    public async Task Canonical_google_start_redirects_only_to_the_google_provider_and_hardens_the_browser_response()
    {
        const string providerTarget =
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=test&state=provider-state";
        var handler = new RecordingHandler(_ => Redirect(providerTarget));
        using var client = new HttpClient(handler);
        var proxy = new AuthorizationFlowStartProxy(
            client,
            new AuthorizationFlowProxyOptions(new Uri("https://kernel.internal")));
        using var services = Services();
        var context = Context(OAuthCallbackPaths.GoogleStart, $"?f={FlowReference}", services);

        var result = await proxy.StartAsync(
            OAuthCallbackPaths.GoogleProvider,
            context.Request,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(
            $"https://kernel.internal/oauth/start/google?f={FlowReference}",
            handler.RequestUri?.AbsoluteUri);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(providerTarget, context.Response.Headers.Location.ToString());
        AssertHardened(context.Response);
    }

    [Theory]
    [InlineData("https://evil.example/oauth/authorize")]
    [InlineData("https://login.salesforce.com/services/oauth2/authorize?client_id=test")]
    [InlineData("https://accounts.google.com:444/o/oauth2/v2/auth?client_id=test")]
    public async Task Provider_mismatch_or_untrusted_redirect_is_rejected(string providerTarget)
    {
        var handler = new RecordingHandler(_ => Redirect(providerTarget));
        using var client = new HttpClient(handler);
        var proxy = new AuthorizationFlowStartProxy(
            client,
            new AuthorizationFlowProxyOptions(new Uri("https://kernel.internal")));
        using var services = Services();
        var context = Context(OAuthCallbackPaths.GoogleStart, $"?f={FlowReference}", services);

        var result = await proxy.StartAsync(
            OAuthCallbackPaths.GoogleProvider,
            context.Request,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        AssertHardened(context.Response);
    }

    [Fact]
    public async Task Malformed_flow_is_rejected_without_contacting_the_internal_runtime()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("The handler must not run."));
        using var client = new HttpClient(handler);
        var proxy = new AuthorizationFlowStartProxy(
            client,
            new AuthorizationFlowProxyOptions(new Uri("https://kernel.internal")));
        using var services = Services();
        var context = Context(OAuthCallbackPaths.GoogleStart, "?f=short", services);

        var result = await proxy.StartAsync(
            OAuthCallbackPaths.GoogleProvider,
            context.Request,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Null(handler.RequestUri);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        AssertHardened(context.Response);
    }

    [Fact]
    public async Task Internal_transport_failure_returns_a_hardened_safe_gateway_error()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("sensitive upstream detail"));
        using var client = new HttpClient(handler);
        var proxy = new AuthorizationFlowStartProxy(
            client,
            new AuthorizationFlowProxyOptions(new Uri("https://kernel.internal")));
        using var services = Services();
        var context = Context(OAuthCallbackPaths.GoogleStart, $"?f={FlowReference}", services);

        var result = await proxy.StartAsync(
            OAuthCallbackPaths.GoogleProvider,
            context.Request,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        AssertHardened(context.Response);
    }

    [Fact]
    public void Production_requires_an_https_internal_runtime_origin()
    {
        var missing = Configuration(new Dictionary<string, string?>());
        Assert.Throws<InvalidOperationException>(() =>
            AuthorizationFlowProxyOptions.FromConfiguration(missing, RuntimeProfile.Production));

        var plaintext = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:OAuth:InternalOrigin"] = "http://kernel.internal"
        });
        Assert.Throws<InvalidOperationException>(() =>
            AuthorizationFlowProxyOptions.FromConfiguration(plaintext, RuntimeProfile.Production));

        var production = AuthorizationFlowProxyOptions.FromConfiguration(
            Configuration(new Dictionary<string, string?>
            {
                ["DigitalBrain:Runtime:OAuth:InternalOrigin"] = "https://kernel.internal"
            }),
            RuntimeProfile.Production);
        Assert.Equal("https://kernel.internal/", production.InternalOrigin.AbsoluteUri);
    }

    private static HttpResponseMessage Redirect(string target)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(target);
        return response;
    }

    private static DefaultHttpContext Context(
        string path,
        string query,
        IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    private static ServiceProvider Services() =>
        new ServiceCollection().AddLogging().BuildServiceProvider();

    private static IConfigurationRoot Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static void AssertHardened(HttpResponse response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", response.Headers.Pragma.ToString());
        Assert.Equal("no-referrer", response.Headers["Referrer-Policy"].ToString());
        Assert.Equal(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
            response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", response.Headers.XContentTypeOptions.ToString());
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(response(request));
        }
    }
}
