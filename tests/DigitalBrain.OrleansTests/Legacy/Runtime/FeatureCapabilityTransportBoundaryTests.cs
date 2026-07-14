using DigitalBrain.RuntimeHost;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Tests.Runtime;

public sealed class FeatureCapabilityTransportBoundaryTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Invalid_token_is_rejected_before_body_size_or_endpoint_binding()
    {
        var nextCalls = 0;
        var boundary = Boundary(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = Request("invalid", 1_000_000);

        await boundary.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task Authenticated_body_over_limit_is_rejected_before_endpoint_binding()
    {
        var nextCalls = 0;
        var boundary = Boundary(_ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = Request(Token, 70 * 1024 + 1);

        await boundary.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Equal(0, nextCalls);
    }

    [Fact]
    public async Task Authenticated_bounded_request_reaches_endpoint_with_proof_marker()
    {
        var nextCalls = 0;
        var boundary = Boundary(context =>
        {
            nextCalls++;
            Assert.Contains(context.Items, pair => pair.Value is true);
            return Task.CompletedTask;
        });
        var context = Request(Token, 1_024);

        await boundary.InvokeAsync(context);

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task Chunked_request_receives_the_same_body_limit_before_endpoint_binding()
    {
        var bodySize = new MutableBodySizeFeature();
        var boundary = Boundary(_ => Task.CompletedTask);
        var context = Request(Token, null);
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(bodySize);

        await boundary.InvokeAsync(context);

        Assert.Equal(70 * 1024, bodySize.MaxRequestBodySize);
    }

    [Fact]
    public async Task Seventeenth_parallel_request_is_rejected()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var boundary = Boundary(async _ =>
        {
            if (Interlocked.Increment(ref count) == 16) entered.SetResult();
            await release.Task;
        });
        var admitted = Enumerable.Range(0, 16)
            .Select(_ => boundary.InvokeAsync(Request(Token, 1_024)))
            .ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var overflow = Request(Token, 1_024);

        await boundary.InvokeAsync(overflow);
        release.SetResult();
        await Task.WhenAll(admitted);

        Assert.Equal(StatusCodes.Status429TooManyRequests, overflow.Response.StatusCode);
    }

    [Fact]
    public async Task Two_hundred_and_forty_first_request_in_window_is_rejected()
    {
        var calls = 0;
        var boundary = Boundary(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });
        for (var index = 0; index < 240; index++)
            await boundary.InvokeAsync(Request(Token, 1_024));
        var overflow = Request(Token, 1_024);

        await boundary.InvokeAsync(overflow);

        Assert.Equal(240, calls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, overflow.Response.StatusCode);
    }

    [Fact]
    public void Capability_transport_has_no_payload_log_or_audit_sink()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "hosts",
            "DigitalBrain.RuntimeHost",
            "Program.cs"));
        var start = source.IndexOf("private static void MapFeatureCapabilities", StringComparison.Ordinal);
        var end = source.IndexOf("private static void ConfigureKestrel", start, StringComparison.Ordinal);
        var transport = source[start..end];

        Assert.DoesNotContain("ILogger", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("LogInformation", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning", transport, StringComparison.Ordinal);
        Assert.DoesNotContain("Audit", transport, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeHostExtensions.FeatureCapabilityTransportBoundary Boundary(RequestDelegate next)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:FeatureHost:InternalToken"] = Token
            })
            .Build();
        return new RuntimeHostExtensions.FeatureCapabilityTransportBoundary(
            next,
            configuration,
            TimeProvider.System);
    }

    private static DefaultHttpContext Request(string token, long? contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/internal/features/capabilities/execute";
        context.Request.ContentLength = contentLength;
        context.Request.Headers["X-DigitalBrain-Internal-Token"] = token;
        return context;
    }

    private sealed class MutableBodySizeFeature : Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
