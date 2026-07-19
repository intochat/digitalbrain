using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BrainHostingContracts
{
    private const string SecretValue = "sk-live-must-never-reach-a-client";

    [Fact]
    public async Task TheSiloProjectionCarriesTheDeclaredModelTiers()
    {
        var environment = await ProjectAsync(privileged: true);

        Assert.Contains(environment, entry => entry.Key.EndsWith("__Tier", StringComparison.Ordinal));
        Assert.Contains(environment, entry => entry.Key.EndsWith("__ModelId", StringComparison.Ordinal));
        Assert.Contains(environment, entry => entry.Key.EndsWith("__ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheClientProjectionCarriesNoModelBindingAtAll()
    {
        var environment = await ProjectAsync(privileged: false);

        Assert.DoesNotContain(environment, entry => entry.Key.StartsWith(BrainHostingExtensions.ModelConfigurationPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NoProjectionEverCarriesASecretLiteral()
    {
        foreach (var privileged in (bool[])[true, false])
        {
            var environment = await ProjectAsync(privileged);

            Assert.DoesNotContain(environment, entry => entry.Value.Contains(SecretValue, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TheClientProjectionStillDiscoversTheCluster()
    {
        var environment = await ProjectAsync(privileged: false);

        Assert.Contains(environment, entry => entry.Key.StartsWith("Orleans__ClusterId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSiloProjectionRoundTripsIntoKernelModelBindings()
    {
        var projected = await ProjectAsync(privileged: true);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(projected.Select(entry =>
                new KeyValuePair<string, string?>(entry.Key.Replace("__", ":", StringComparison.Ordinal), entry.Value)))
            .Build();

        var bound = ModelConfiguration.Read(configuration).ToList();

        var balanced = Assert.Single(bound);
        Assert.Equal(ModelTier.Balanced, balanced.Tier);
        Assert.Equal(ModelDescriptor.OpenAiProvider, balanced.Provider);
        Assert.Equal("small", balanced.ModelId);
    }

    [Fact]
    public void EachTierBindsToExactlyOneModelOnTheResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var key = builder.AddParameter("provider-key", SecretValue, secret: true);
        var brain = builder.AddBrain("brain").WithDevelopmentStores();

        brain.WithModel(ModelTier.Balanced, ModelDescriptor.OpenAiProvider, "small", key);

        Assert.Throws<InvalidOperationException>(
            () => brain.WithModel(ModelTier.Balanced, ModelDescriptor.AnthropicProvider, "other", key));
    }

    private static async Task<Dictionary<string, string>> ProjectAsync(bool privileged)
    {
        var builder = DistributedApplication.CreateBuilder();
        var key = builder.AddParameter("provider-key", SecretValue, secret: true);
        var brain = builder.AddBrain("brain").WithDevelopmentStores();

        brain.WithModel(ModelTier.Balanced, ModelDescriptor.OpenAiProvider, "small", key);

        var consumer = builder.AddResource(new ProjectionProbe("consumer"));

        if (privileged)
        {
            consumer.WithReference(brain);
        }
        else
        {
            consumer.WithReference(brain.AsClient());
        }

        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in consumer.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToString() ?? string.Empty,
            StringComparer.Ordinal);
    }

    private sealed class ProjectionProbe(string name) : Resource(name), IResourceWithEnvironment, IResourceWithEndpoints;
}
