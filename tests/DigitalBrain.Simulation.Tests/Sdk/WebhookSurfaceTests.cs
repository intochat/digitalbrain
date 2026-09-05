using System.Text;
using DigitalBrain.Sdk.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Sdk;

public sealed class WebhookSurfaceTests
{
    [Fact]
    public async Task SuccessWaitsForAcceptanceAndPreservesExactBytesAndDuplicateHeaders()
    {
        var accepted = new TaskCompletionSource<WebhookAcceptance>(TaskCreationOptions.RunContinuationsAsynchronously);
        WebhookRequest? received = null;
        var pipeline = Pipeline(new Handler((request, _) =>
        {
            received = request;
            return accepted.Task;
        }));
        var context = Request("{\n \"message\": \"héllo\"\n}");
        context.Request.Headers["X-Delivery"] = new[] { "first", "second" };
        var running = pipeline(context);
        Assert.False(running.IsCompleted);
        Assert.Equal(Encoding.UTF8.GetBytes("{\n \"message\": \"héllo\"\n}"), received!.Body.ToArray());
        Assert.Equal(new[] { "first", "second" }, received.Headers["x-delivery"]);
        accepted.SetResult(WebhookAcceptance.Accepted);
        await running;
        Assert.Equal(202, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData(WebhookAcceptance.Duplicate, 200)]
    [InlineData(WebhookAcceptance.Ignored, 204)]
    [InlineData(WebhookAcceptance.BadRequest, 400)]
    [InlineData(WebhookAcceptance.Unauthorized, 401)]
    [InlineData(WebhookAcceptance.Conflict, 409)]
    [InlineData(WebhookAcceptance.Unavailable, 503)]
    public async Task MapsProviderAcceptanceWithoutPayloadEcho(WebhookAcceptance result, int expected)
    {
        var context = Request("{}");
        await Pipeline(new Handler((_, _) => Task.FromResult(result)))(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("GET", "application/json", "{}", 405)]
    [InlineData("POST", "text/plain", "{}", 415)]
    [InlineData("POST", "application/json", "123456789", 413)]
    public async Task RejectsInvalidTransportBeforeCallingProvider(string method, string contentType, string body, int status)
    {
        var calls = 0;
        var pipeline = Pipeline(new Handler((_, _) =>
        {
            calls++;
            return Task.FromResult(WebhookAcceptance.Accepted);
        }), maxBodyBytes: 8);
        var context = Request(body);
        context.Request.Method = method;
        context.Request.ContentType = contentType;
        context.Request.ContentLength = null; // An unbounded/chunked request must still hit the limit.
        await pipeline(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TimesOutEvenWhenProviderIgnoresCancellationAndDoesNotExposeExceptionContent()
    {
        var never = new TaskCompletionSource<WebhookAcceptance>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = Request("{}");
        await Pipeline(new Handler((_, _) => never.Task), timeout: TimeSpan.FromMilliseconds(20))(context);
        Assert.Equal(503, context.Response.StatusCode);
        var failed = Request("{}");
        await Pipeline(new Handler((_, _) => throw new InvalidOperationException("private provider credential")))(failed);
        Assert.Equal(503, failed.Response.StatusCode);
        Assert.Equal(0, failed.Response.Body.Length);
        never.TrySetResult(WebhookAcceptance.Accepted);
    }

    [Fact]
    public async Task OnlyConsumesConfiguredRoute()
    {
        var context = Request("{}");
        context.Request.Path = "/webhooks/github/extra";
        await Pipeline(new Handler((_, _) => throw new InvalidOperationException()))(context);
        Assert.Equal(404, context.Response.StatusCode);
    }

    private static DefaultHttpContext Request(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/webhooks/github";
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json; charset=utf-8";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RequestDelegate Pipeline(IWebhookHandler handler, int maxBodyBytes = 1024, TimeSpan? timeout = null)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        new WebhookSurface(new WebhookDefinition("/webhooks/github", maxBodyBytes, timeout), handler).Map(app);
        app.Run(context =>
        {
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        });
        return app.Build();
    }

    private sealed class Handler(Func<WebhookRequest, CancellationToken, Task<WebhookAcceptance>> handle) : IWebhookHandler
    {
        public Task<WebhookAcceptance> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
            => handle(request, cancellationToken);
    }
}
