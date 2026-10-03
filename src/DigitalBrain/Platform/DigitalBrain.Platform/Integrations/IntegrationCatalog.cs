using DigitalBrain.Platform.Contracts.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

internal static class IntegrationCatalog
{
    public static async Task<IntegrationCatalogEntry[]> ListAsync(
        IReadOnlyList<IntegrationDefinition> definitions, IGrainFactory grains, CancellationToken cancellationToken)
    {
        var entries = new List<IntegrationCatalogEntry>(definitions.Count);
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await grains.GetGrain<IIntegrationRegistration>(IntegrationVault.GrainKeyPrefix + definition.Id).Read();
            entries.Add(new IntegrationCatalogEntry(definition.Id, definition.DisplayName, snapshot.Status.ToString(), snapshot.MissingFields));
        }

        return [.. entries];
    }
}
