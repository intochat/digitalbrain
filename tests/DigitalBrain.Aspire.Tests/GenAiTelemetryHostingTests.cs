using DigitalBrain.Testing.E2E;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

public sealed class GenAiTelemetryHostingTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task HostOptInProjectsConsistentFlagsToTheKernelWithoutClientOverrides(string enabled)
    {
        await using var model = await BrainModel.BuildAsync<Projects.DigitalBrain_AppHost>(
            $"--DigitalBrain:AI:Telemetry:EnableSensitiveData={enabled}");

        var environment = await model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);
        Assert.Equal(enabled, environment["DigitalBrain__AI__Telemetry__EnableSensitiveData"]);
        Assert.Equal(enabled, environment["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"]);

        // Client-only processes do not host the model pipeline or receive module
        // projections. They must not retain the previous contradictory hardcoded flag.
        foreach (var resource in new[] { ProductSurfaceResourceNames.Mcp, ProductSurfaceResourceNames.Scripting })
        {
            var clientEnvironment = await model.RenderedEnvironmentAsync(resource);
            Assert.DoesNotContain("DigitalBrain__AI__Telemetry__EnableSensitiveData", clientEnvironment.Keys);
            Assert.DoesNotContain("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", clientEnvironment.Keys);
        }
    }
}
