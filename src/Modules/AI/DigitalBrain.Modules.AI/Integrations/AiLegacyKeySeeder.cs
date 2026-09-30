using DigitalBrain.Sdk.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.AI;

// Deployments that still project DigitalBrain:AI:{Provider}:ApiKey (and Tavily's key) keep working: those keys
// only seed an Unconfigured registration, after the canonical DigitalBrain:Integrations seed has had its turn.
internal sealed class AiLegacyKeySeeder(IConfiguration configuration, IGrainFactory grains, ILogger<AiLegacyKeySeeder> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var options = AIOptions.Read(configuration);
        foreach (var provider in AiIntegrations.KeyedProviders)
        {
            var apiKey = configuration[$"{AIClients.ConfigurationRoot}:{provider}:{AiIntegrations.ApiKeyField}"];
            var endpoint = options.Provider(provider).Endpoint;
            await SeedAsync(AiIntegrations.IdOf(provider), apiKey,
                (AiIntegrations.EndpointField, string.IsNullOrWhiteSpace(endpoint) ? AiIntegrations.DefaultEndpointOf(provider) : endpoint), cancellationToken);
        }

        await SeedAsync(AiIntegrations.TavilyId, configuration[$"{AIClients.ConfigurationRoot}:Tavily:{AiIntegrations.ApiKeyField}"], null, cancellationToken);
    }

    private async Task SeedAsync(string integrationId, string? apiKey, (string Field, string Value)? setting, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var values = new Dictionary<string, string> { [AiIntegrations.ApiKeyField] = apiKey };
        if (setting is { } pair)
        {
            values[pair.Field] = pair.Value;
        }

        try
        {
            var after = await grains.GetGrain<IIntegrationRegistration>("integration/" + integrationId)
                .SeedIfUnconfigured(new ConfigureRegistration { Values = values });
            logger.LogInformation("Integration {IntegrationId} is {Status} after seeding from DigitalBrain:AI.", integrationId, after.Status);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(error, "Seeding integration {IntegrationId} from DigitalBrain:AI failed.", integrationId);
        }
    }
}
