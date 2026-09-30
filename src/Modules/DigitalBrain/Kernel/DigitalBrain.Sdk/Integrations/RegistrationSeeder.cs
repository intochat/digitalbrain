using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.Sdk.Integrations;

// Seeds DigitalBrain:Integrations:{id}:{Field} into registrations that are still Unconfigured. A
// Partial registration is an operator's edit in progress and is never touched.
internal sealed class RegistrationSeeder(
    IConfiguration configuration,
    IReadOnlyList<IntegrationDefinition> definitions,
    IGrainFactory grains,
    ILogger<RegistrationSeeder> logger) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken) => SeedAsync(cancellationToken);

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SeedOneAsync(definition);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(error, "Seeding integration {IntegrationId} failed.", definition.Id);
            }
        }
    }

    private async Task SeedOneAsync(IntegrationDefinition definition)
    {
        var values = definition.AllFields
            .Select(field => (Field: field, Value: configuration[$"DigitalBrain:Integrations:{definition.Id}:{field}"]))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Field, pair => pair.Value!, StringComparer.Ordinal);
        if (values.Count == 0)
        {
            return;
        }

        var registration = grains.GetGrain<IIntegrationRegistration>(IntegrationVault.GrainKeyPrefix + definition.Id);
        if ((await registration.Read()).Status != RegistrationStatus.Unconfigured)
        {
            return;
        }

        await registration.Configure(new ConfigureRegistration { Values = values });
        logger.LogInformation("Seeded integration {IntegrationId} with {FieldCount} fields.", definition.Id, values.Count);
    }
}
