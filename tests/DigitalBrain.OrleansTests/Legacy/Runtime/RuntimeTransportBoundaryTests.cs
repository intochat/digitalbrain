extern alias McpProject;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RuntimeTransportBoundary = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundary;
using RuntimeTransportBoundaryOptions = McpProject::DigitalBrain.Mcp.RuntimeTransportBoundaryOptions;

namespace DigitalBrain.Tests.Runtime;

public sealed class RuntimeTransportBoundaryTests
{
    [Fact]
    public void Configuration_preserves_the_operator_body_limit_for_MCP_and_OAuth()
    {
        var fallback = RuntimeTransportBoundaryOptions.FromConfiguration(new ConfigurationBuilder().Build());
        var staleSixMiB = RuntimeTransportBoundaryOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Runtime:Transport:MaxBodyBytes"] = "6291456"
            })
            .Build());

        Assert.Equal(2 * 1024 * 1024, fallback.MaximumBodyBytes);
        Assert.Equal(6 * 1024 * 1024, staleSixMiB.MaximumBodyBytes);
    }

    [Fact]
    public async Task InvokeAsync_defers_UI_gRPC_message_size_to_the_protocol_aware_gRPC_limit()
    {
        var options = new RuntimeTransportBoundaryOptions(
            MaximumBodyBytes: 1024,
            MaximumConcurrentRequests: 4,
            RequestsPerMinute: 100,
            RequestTimeout: TimeSpan.FromSeconds(30));
        var bodyFeature = new FakeMaxRequestBodySizeFeature();
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/digitalbrain.v2.ui.DigitalBrainV2Ui/ReviseFeatureDraft",
                Scheme = "https",
                ContentLength = 2048
            }
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(bodyFeature);
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

        Assert.Null(bodyFeature.MaxRequestBodySize);
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_sets_the_Kestrel_body_size_feature_even_when_no_Content_Length_header_is_present()
    {

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
    public async Task InvokeAsync_rejects_an_oversized_MCP_Content_Length_before_the_handler()
    {
        var options = new RuntimeTransportBoundaryOptions(
            MaximumBodyBytes: 1024,
            MaximumConcurrentRequests: 4,
            RequestsPerMinute: 100,
            RequestTimeout: TimeSpan.FromSeconds(30));
        var bodyFeature = new FakeMaxRequestBodySizeFeature();
        var context = new DefaultHttpContext
        {
            Request =
            {
                Path = "/mcp",
                Scheme = "https",
                ContentLength = 1025
            }
        };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(bodyFeature);
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
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
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
