using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Memory.Qdrant;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class MemoryModuleComposition
{
    private const string BrainName = "brain";

    [Fact(DisplayName =
        "WithQdrant selects MemoryModule metadata and projects the Qdrant provider onto the silo only")]
    public async Task WithQdrantProjectsModuleMetadataAndProviderResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain(BrainName);
        brain.AddModule<MemoryModule>(memory => memory.WithQdrant());

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var client = builder
            .AddContainer("client", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain.AsClient());

        var qdrantName = $"{BrainName}-memory-qdrant";
        var qdrant = Assert.Single(
            builder.Resources,
            resource => string.Equals(resource.Name, qdrantName, StringComparison.Ordinal));
        Assert.IsAssignableFrom<QdrantServerResource>(qdrant);

        var siloEnvironment = await EnvironmentOf(silo.Resource).ConfigureAwait(true);
        var clientEnvironment = await EnvironmentOf(client.Resource).ConfigureAwait(true);

        Assert.Equal(
            MemoryModule.Id.Value,
            siloEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__0"]
                ?.ToString());
        Assert.Equal(
            MemoryModule.Id.Value,
            clientEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__0"]
                ?.ToString());

        Assert.Equal(
            MemoryModule.QdrantProviderName,
            siloEnvironment["DigitalBrain__Memory__Provider"]?.ToString());
        Assert.Equal(
            QdrantVectorMemoryRegistration.DefaultConnectionName,
            siloEnvironment["DigitalBrain__Memory__Qdrant__ConnectionName"]?.ToString());
        Assert.Contains(
            siloEnvironment.Keys,
            key => key.StartsWith(
                $"ConnectionStrings__{QdrantVectorMemoryRegistration.DefaultConnectionName}",
                StringComparison.Ordinal));

        Assert.DoesNotContain("DigitalBrain__Memory__Provider", clientEnvironment.Keys);
        Assert.DoesNotContain("DigitalBrain__Memory__Qdrant__ConnectionName", clientEnvironment.Keys);
        Assert.DoesNotContain(
            clientEnvironment.Keys,
            key => key.Contains("memory-qdrant", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Qdrant", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            silo.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && string.Equals(wait.Resource.Name, qdrantName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && string.Equals(wait.Resource.Name, qdrantName, StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "MemoryModule without WithQdrant is vocabulary-only — selected metadata, no Qdrant resource")]
    public async Task VocabularyOnlySelectionProjectsNoQdrantResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain(BrainName);
        brain.AddModule<MemoryModule>();

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name.Contains("memory-qdrant", StringComparison.OrdinalIgnoreCase)
                || resource is QdrantServerResource);

        var siloEnvironment = await EnvironmentOf(silo.Resource).ConfigureAwait(true);
        Assert.Equal(
            MemoryModule.Id.Value,
            siloEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__0"]
                ?.ToString());
        Assert.DoesNotContain("DigitalBrain__Memory__Provider", siloEnvironment.Keys);
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);

    private static async Task<Dictionary<string, object>> EnvironmentOf(ContainerResource resource)
    {
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return new Dictionary<string, object>(context.EnvironmentVariables, StringComparer.Ordinal);
    }
}
