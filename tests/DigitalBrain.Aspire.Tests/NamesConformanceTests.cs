using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// Single-source-of-truth conformance (spec section 4): hosting-side resource names
// (DigitalBrainNames, wired via WithReference/WithClustering/etc.) and runtime-side keyed-client
// connection-string keys must agree. Model-only, no containers, milliseconds.
[Collection(ModelCollection.Name)]
public sealed class NamesConformanceTests(ModelFixture fixture)
{
    [Fact]
    public async Task KernelRenderedEnvironmentContainsTheSelectedEmbeddingModel()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.ContainsKey("DigitalBrain__AI__Ollama__IEmbeddingGemma__Model"));
    }

    [Theory]
    [InlineData("DigitalBrain__AI__OpenAI__ApiKey")]
    [InlineData("DigitalBrain__AI__Anthropic__ApiKey")]
    [InlineData("DigitalBrain__AI__Google__ApiKey")]
    [InlineData("DigitalBrain__AI__XAI__ApiKey")]
    public async Task KernelRenderedEnvironmentWiresEveryCloudProviderApiKey(string configurationKey)
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.ContainsKey(configurationKey));
    }

    [Fact]
    public async Task KernelRenderedEnvironmentContainsTheExplicitAppHostModuleManifest()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        var modules = environment
            .Where(static pair => pair.Key.StartsWith("DigitalBrain__Modules__", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Value)
            .ToArray();

        Assert.Equal(
            [
                "DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI",
                "DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory",
                "DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time",
                "DigitalBrain.Execution.ExecutionModule, DigitalBrain.Modules.Execution",
                "DigitalBrain.Integrations.IntegrationsModule, DigitalBrain.Modules.Integrations",
                "DigitalBrain.SmartPrompt.SmartPromptModule, DigitalBrain.Modules.SmartPrompt",
                "DigitalBrain.UI.UIModule, DigitalBrain.Modules.UI",
            ],
            modules);
    }

    [Theory]
    [InlineData(DigitalBrainNames.Clustering)]
    [InlineData(DigitalBrainNames.Reminders)]
    [InlineData(DigitalBrainNames.Journal)]
    [InlineData(DigitalBrainNames.GrainState)]
    public async Task KernelRenderedEnvironmentContainsFabricConnectionStringKey(string fabricResourceName)
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);
        var expectedKey = $"ConnectionStrings__{fabricResourceName}";

        Assert.Contains(
            environment.Keys,
            key => string.Equals(key, expectedKey, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WithBrainTestModeStampsTestingModeEnvironmentVariable()
    {
        // Throwaway builder/resource, unrelated to the shared AppHost model: this exercises the
        // WithBrainTestMode() SDK helper itself, not product topology. AddExecutable + the same
        // ExecutionConfigurationBuilder rendering path BrainModel.RenderedEnvironmentAsync uses
        // keeps this model-only — building it never starts anything.
        var builder = DistributedApplication.CreateBuilder([]);
        var throwaway = builder.AddExecutable("throwaway", "true", ".").WithBrainTestMode();

        var configuration = await ExecutionConfigurationBuilder.Create(throwaway.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
        var environment = configuration.EnvironmentVariables.ToDictionary();

        Assert.True(environment.TryGetValue("DigitalBrain__Mode", out var mode));
        Assert.Equal(DigitalBrainNames.TestingMode, mode);
    }

    [Fact]
    public async Task WithDigitalBrainFakesStampsFakesEnabledEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var throwaway = builder.AddExecutable("throwaway", "true", ".")
            .WithDigitalBrainFakes();

        var configuration = await ExecutionConfigurationBuilder.Create(throwaway.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
        var environment = configuration.EnvironmentVariables.ToDictionary();

        Assert.True(environment.TryGetValue("DigitalBrain__Fakes__Enabled", out var enabled));
        Assert.Equal("true", enabled);
    }
}
