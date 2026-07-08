using DigitalBrain.Core;

namespace DigitalBrain.Ino;

public static class InoCapabilityCatalog
{
    public static async Task<IReadOnlyList<InoCapabilityRecord>> LoadAsync(
        IGrainFactory grains,
        IEnumerable<Synapse> localJournal,
        IEnumerable<InoCapabilityRecord> handlerCapabilities,
        CancellationToken cancellationToken)
    {
        var records = new List<InoCapabilityRecord>();
        records.AddRange(InoAgentCapabilities.DiscoverAgentRecords());
        records.AddRange(handlerCapabilities);
        records.AddRange(localJournal.OfType<CapabilityRegistered>().Select(FromRegistered));

        try
        {
            var automation = grains.GetGrain<IAutomationNeuron>("automation-main");
            var automationTimeline = await automation.GetTimelineAsync(cancellationToken);
            records.AddRange(automationTimeline.OfType<CapabilityRegistered>().Select(FromRegistered));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // The automation catalog slice is optional in small test hosts.
        }

        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .GroupBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(CatalogPriority).ThenBy(record => record.Origin, StringComparer.Ordinal).First())
            .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static InoCapabilityRecord FromRegistered(CapabilityRegistered capability) =>
        new(
            InoAgentCapabilities.NormalizeId(capability.Id),
            capability.Id,
            capability.Description,
            capability.Examples,
            capability.Examples,
            capability.Tier,
            capability.Origin ?? "CapabilityRegistered",
            "CapabilityRegistered",
            "JournalFact");

    private static int CatalogPriority(InoCapabilityRecord record) =>
        record.SourceKind switch
        {
            InoAgentCapabilities.AgentSourceKind => 4,
            "CapabilityRegistered" => 3,
            "InoHandler" => 2,
            _ => 1
        };
}
