using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
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
    public async Task KernelRenderedEnvironmentContainsEmbeddingsModelKey()
    {
        // Pins the phase-3 production embeddings opt-in: WithEmbeddings() on the AI module must
        // project the Ollama embeddings model tag so AIClients can gate IEmbeddingGenerator
        // registration on it (CapabilityIndex.FindAsync switches to hybrid ranking automatically).
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(
            environment.Keys,
            key => string.Equals(key, "DigitalBrain__AI__Ollama__Embeddings__Model", StringComparison.OrdinalIgnoreCase));
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
}
