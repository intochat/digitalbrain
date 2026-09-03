using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Testing.E2E;
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
    [InlineData("DigitalBrain__AI__Default__Model", "IGpt56Luna")]
    [InlineData("DigitalBrain__AI__Default__Embedding", "ITextEmbedding3Small")]
    public async Task KernelRenderedEnvironmentContainsTheSelectedOpenAIDefaults(
        string configurationKey,
        string expectedModel)
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.TryGetValue(configurationKey, out var configuredModel));
        Assert.Equal(expectedModel, configuredModel);
    }

    [Fact]
    public async Task KernelRenderedEnvironmentWiresTheConfiguredOpenAIApiKey()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.Contains("DigitalBrain__AI__OpenAI__ApiKey", environment.Keys);
    }

    [Fact]
    public async Task KernelRenderedEnvironmentContainsTheExplicitAppHostModuleManifest()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        // Sorted by the numeric index suffix, not ordinally: an ordinal key sort would place
        // DigitalBrain__Modules__10 before __2 once the manifest reaches eleven modules.
        // Non-index keys (e.g. a future DigitalBrain__Modules__0__Type) are filtered out
        // rather than crashing the parse.
        const string ModuleKeyPrefix = "DigitalBrain__Modules__";
        var modules = environment
            .Where(static pair => pair.Key.StartsWith(ModuleKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => (Suffix: pair.Key[ModuleKeyPrefix.Length..], pair.Value))
            .Where(static pair => int.TryParse(pair.Suffix, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(static pair => int.Parse(pair.Suffix, CultureInfo.InvariantCulture))
            .Select(static pair => pair.Value)
            .ToArray();

        Assert.Equal(
            [
                "DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI",
                "DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory",
                "DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time",
                "DigitalBrain.Excel.ExcelModule, DigitalBrain.Modules.Excel",
                "DigitalBrain.Execution.ExecutionModule, DigitalBrain.Modules.Execution",
                "DigitalBrain.Google.GoogleModule, DigitalBrain.Modules.Google",
                "DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce",
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
        var environment = await RenderStampedThrowawayAsync(static throwaway => throwaway.WithBrainTestMode());

        Assert.True(environment.TryGetValue("DigitalBrain__Mode", out var mode));
        Assert.Equal(DigitalBrainNames.TestingMode, mode);
    }

    [Fact]
    public async Task WithDigitalBrainFakesStampsFakesEnabledEnvironmentVariable()
    {
        var environment = await RenderStampedThrowawayAsync(static throwaway => throwaway.WithDigitalBrainFakes());

        Assert.True(environment.TryGetValue("DigitalBrain__Fakes__Enabled", out var enabled));
        Assert.Equal("true", enabled);
    }

    [Theory]
    [InlineData("DigitalBrain__Google__Gmail__OAuth__ClientId", "gmail-client-id")]
    [InlineData("DigitalBrain__Google__Gmail__OAuth__ClientSecret", "gmail-client-secret")]
    [InlineData("DigitalBrain__Salesforce__OAuth__ConsumerKey", "salesforce-consumer-key")]
    [InlineData("DigitalBrain__Salesforce__OAuth__ConsumerSecret", "salesforce-consumer-secret")]
    public async Task KernelRenderedEnvironmentWiresModuleOwnedOAuthParameters(string key, string parameterName)
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.TryGetValue(key, out var value));
        Assert.Contains(parameterName, value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DigitalBrain__Google__Gmail__OAuth__PublicOrigin")]
    [InlineData("DigitalBrain__Salesforce__OAuth__PublicOrigin")]
    public async Task KernelRenderedEnvironmentDerivesPublicOriginFromItsOwnHttpEndpoint(string key)
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.TryGetValue(key, out var origin));
        Assert.Contains(ProductSurfaceResourceNames.Kernel, origin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KernelRenderedEnvironmentContainsTheHostedSalesforceMcpEndpoint()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.TryGetValue("DigitalBrain__Salesforce__Mcp__Endpoint", out var endpoint));
        Assert.Equal("https://api.salesforce.com/platform/mcp/v1/platform/sobject-all", endpoint);
    }

    // Throwaway builder/resource, unrelated to the shared AppHost model: these two tests exercise
    // an SDK stamping helper itself, not product topology. AddExecutable + the same
    // ExecutionConfigurationBuilder rendering path BrainModel.RenderedEnvironmentAsync uses keeps
    // this model-only — building it never starts anything.
    private static async Task<Dictionary<string, string>> RenderStampedThrowawayAsync(
        Func<IResourceBuilder<ExecutableResource>, IResourceBuilder<ExecutableResource>> stamp)
    {
        var builder = DistributedApplication.CreateBuilder([]);
        var throwaway = stamp(builder.AddExecutable("throwaway", "true", "."));

        var configuration = await ExecutionConfigurationBuilder.Create(throwaway.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new(DistributedApplicationOperation.Publish),
                NullLogger.Instance,
                TestContext.Current.CancellationToken);
        return configuration.EnvironmentVariables.ToDictionary();
    }
}
