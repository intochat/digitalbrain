using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.Shell.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class HostingProjectionContracts
{
    private static readonly string[] SiloOnlyEnvironmentKeys =
    [
        ShellHostingProjectionSupport.JournalConnectionEnvironmentKey,
        ConfigurationEnvironment(DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey),
        ConfigurationEnvironment("DigitalBrain:AI:Ollama:Endpoint"),
        ConfigurationEnvironment("DigitalBrain:Google:Gmail:ClientId"),
        ConfigurationEnvironment("DigitalBrain:Salesforce:ClientId"),
    ];

    [Fact(DisplayName =
        "complete and client references share module topology while only the complete reference receives secrets")]
    public async Task ReferencesShareModuleTopologyWithoutLeakingSiloSecretsToClient()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<AIModule>(ai =>
        {
            ai.EnableSensitiveData = true;
            ai.WithLlm<Llama32>();
        });
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var client = builder
            .AddContainer("client", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain.AsClient());

        var siloEnvironment = await ShellHostingProjectionSupport
            .EnvironmentKeysOf(silo.Resource)
            .ConfigureAwait(true);
        var clientEnvironment = await ShellHostingProjectionSupport
            .EnvironmentOf(client.Resource)
            .ConfigureAwait(true);
        var clientEnvironmentKeys = clientEnvironment.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.Contains(key, siloEnvironment));
        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.DoesNotContain(key, clientEnvironmentKeys));
        Assert.Equal(
            AIModule.Id.Value,
            clientEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__0"]
                ?.ToString());
        Assert.Equal(
            GoogleModule.Id.Value,
            clientEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__1"]
                ?.ToString());
        Assert.Equal(
            SalesforceModule.Id.Value,
            clientEnvironment[
                $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__2"]
                ?.ToString());
        Assert.Equal(
            bool.TrueString,
            (await ShellHostingProjectionSupport.EnvironmentOf(silo.Resource).ConfigureAwait(true))[
                "DigitalBrain__AI__Telemetry__EnableSensitiveData"]
                ?.ToString());
        Assert.DoesNotContain(
            "DigitalBrain__AI__Telemetry__EnableSensitiveData",
            clientEnvironmentKeys);
        Assert.DoesNotContain(
            clientEnvironmentKeys,
            key => key.StartsWith("DigitalBrain__ConfiguredFeatures__", StringComparison.Ordinal));

        Assert.Contains(
            silo.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);
        Assert.DoesNotContain(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);

        ShellHostingProjectionSupport.AssertNoOSSurfaceResources(builder);
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
