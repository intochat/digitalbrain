using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class HostingProjectionContracts
{
    private static readonly string[] SiloOnlyEnvironmentKeys =
    [
        FlutterHostingProjectionSupport.JournalConnectionEnvironmentKey,
        ConfigurationEnvironment(DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey),
        $"{ConfigurationEnvironment(DigitalBrainHostingExtensions.ModulesConfigurationKey)}__0",
        ConfigurationEnvironment("DigitalBrain:AI:Ollama:Endpoint"),
        ConfigurationEnvironment("DigitalBrain:Integrations:Mcp:AuthorizationMode"),
        ConfigurationEnvironment("DigitalBrain:Google:Gmail:ClientId"),
        ConfigurationEnvironment("DigitalBrain:Salesforce:ClientId"),
    ];

    [Fact(DisplayName =
        "WithReference(brain) projects journal, state-protection key, AI config, and OAuth; AsClient never does")]
    public async Task CompleteBrainReferenceProjectsSiloOnlySecretsAndClientDoesNot()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UIHttpEndpointName)
            .WithReference(brain);

        var client = builder
            .AddContainer("client", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UIHttpEndpointName)
            .WithReference(brain.AsClient());

        var siloEnvironment = await FlutterHostingProjectionSupport
            .EnvironmentKeysOf(silo.Resource)
            .ConfigureAwait(true);
        var clientEnvironment = await FlutterHostingProjectionSupport
            .EnvironmentKeysOf(client.Resource)
            .ConfigureAwait(true);

        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.Contains(key, siloEnvironment));
        Assert.All(SiloOnlyEnvironmentKeys, key => Assert.DoesNotContain(key, clientEnvironment));

        Assert.Contains(
            silo.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);
        Assert.DoesNotContain(
            client.Resource.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy);

        FlutterHostingProjectionSupport.AssertNoOSSurfaceResources(builder);
    }

    private static string ConfigurationEnvironment(string configurationKey)
        => configurationKey.Replace(":", "__", StringComparison.Ordinal);
}
