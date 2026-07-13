extern alias McpProject;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeTransportBoundary = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundary;
using RuntimeTransportBoundaryOptions = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundaryOptions;

namespace DigitalBrain.Tests.Runtime;

public sealed class RuntimeTransportBoundaryTests
{
    [Fact]
    public async Task InvokeAsync_sets_the_Kestrel_body_size_feature_even_when_no_Content_Length_header_is_present()
    {
        // A chunked-encoding (or otherwise unknown-length) request never populates Content-Length, so the
        // ContentLength-based pre-check can't bound it -- only IHttpMaxRequestBodySizeFeature can, because
        // Kestrel enforces it against the actual bytes read off the stream regardless of how (or whether)
        // the length was declared.
        var options = new RuntimeTransportBoundaryOptions(
            MaximumBodyBytes: 1024,
            MaximumConcurrentRequests: 4,
            RequestsPerMinute: 100,
            RequestTimeout: TimeSpan.FromSeconds(30));
        var bodyFeature = new FakeMaxRequestBodySizeFeature();
        var context = new DefaultHttpContext { Request = { Path = "/mcp", Scheme = "https" } };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(bodyFeature);
        Assert.Null(context.Request.ContentLength);
        var nextCalled = false;
        var boundary = new RuntimeTransportBoundary(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            options,
            TimeProvider.System,
            NullLogger<RuntimeTransportBoundary>.Instance);

        await boundary.InvokeAsync(context);

        Assert.Equal(1024, bodyFeature.MaxRequestBodySize);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_does_not_touch_an_already_read_only_body_size_feature()
    {
        var options = new RuntimeTransportBoundaryOptions(
            MaximumBodyBytes: 1024,
            MaximumConcurrentRequests: 4,
            RequestsPerMinute: 100,
            RequestTimeout: TimeSpan.FromSeconds(30));
        var bodyFeature = new FakeMaxRequestBodySizeFeature { IsReadOnly = true, MaxRequestBodySize = 99 };
        var context = new DefaultHttpContext { Request = { Path = "/mcp", Scheme = "https" } };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(bodyFeature);
        var boundary = new RuntimeTransportBoundary(
            _ => Task.CompletedTask,
            options,
            TimeProvider.System,
            NullLogger<RuntimeTransportBoundary>.Instance);

        await boundary.InvokeAsync(context);

        Assert.Equal(99, bodyFeature.MaxRequestBodySize);
    }

    private sealed class FakeMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; set; }
        public long? MaxRequestBodySize { get; set; }
    }
}
