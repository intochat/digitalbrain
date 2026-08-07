using System.Text.Json;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;

namespace DigitalBrain.Edge;

/// <summary>
/// Reads only known presentation facts from the caller-provided workspace channel.
/// It exposes selected semantic facts and surfaces, never journal records.
/// </summary>
public sealed class WorkspaceUiSurfaceSource : IWorkspaceUiSurfaceSource
{
    private const int DefaultJournalPageSize = 128;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly NeuronId ApprovalProjection = new(
        ApprovalWorkspaceProjectionNeuron.Kind,
        ApprovalWorkspaceInboxNeuron.Name);

    private static readonly string ApprovalSurfaceKind = typeof(ApprovalWorkspaceSurfaceRequested).FullName!;
    private static readonly string SalesSurfaceKind = typeof(SalesInsightSurfaceRequested).FullName!;
    private static readonly string SalesUnavailableKind = typeof(SalesInsightUnavailableSurfaceRequested).FullName!;

    private readonly WorkspaceChannel channel;
    private readonly int journalPageSize;

    public WorkspaceUiSurfaceSource(WorkspaceChannel channel, int journalPageSize = DefaultJournalPageSize)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (journalPageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(journalPageSize), journalPageSize, "A journal page size must be positive.");
        }

        this.channel = channel;
        this.journalPageSize = journalPageSize;
    }

    public async Task<ApprovalWorkspaceSurfaceRequested?> ReadApprovalsAsync(CancellationToken cancellationToken)
    {
        var record = await ReadLatestProducedAsync(
            ApprovalProjection,
            [ApprovalSurfaceKind],
            cancellationToken).ConfigureAwait(false);
        return record is null
            ? null
            : Deserialize<ApprovalWorkspaceSurfaceRequested>(record.Serialization);
    }

    public async Task<UiSurface?> ReadSalesAsync(string queryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        var normalizedQueryId = queryId.Trim();
        var record = await ReadLatestProducedAsync(
            new NeuronId(SalesInsightProjectionNeuron.Kind, normalizedQueryId),
            [SalesSurfaceKind, SalesUnavailableKind],
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        if (string.Equals(record.SynapseKind, SalesSurfaceKind, StringComparison.Ordinal))
        {
            var surface = Deserialize<SalesInsightSurfaceRequested>(record.Serialization);
            return surface is not null
                && string.Equals(surface.QueryId, normalizedQueryId, StringComparison.Ordinal)
                ? BaseUiKitAssembly.SalesReady(surface, record.Position)
                : null;
        }

        var unavailable = Deserialize<SalesInsightUnavailableSurfaceRequested>(record.Serialization);
        return unavailable is not null
            && string.Equals(unavailable.QueryId, normalizedQueryId, StringComparison.Ordinal)
            ? BaseUiKitAssembly.SalesUnavailable(unavailable, record.Position)
            : null;
    }

    private async Task<JournalRecord?> ReadLatestProducedAsync(
        NeuronId neuron,
        IReadOnlyList<string> expectedKinds,
        CancellationToken cancellationToken)
    {
        JournalRecord? latest = null;
        var afterPosition = 0L;

        while (true)
        {
            var read = await channel.Journal.ReadAsync(
                neuron,
                afterPosition,
                journalPageSize,
                cancellationToken).ConfigureAwait(false);
            if (read is JournalHistoryUnavailable)
            {
                return null;
            }

            if (read is not JournalPage page)
            {
                return null;
            }

            foreach (var record in page.Records)
            {
                if (record.Position > afterPosition
                    && record.Direction == JournalRecordDirection.Produced
                    && expectedKinds.Any(kind => string.Equals(
                        kind,
                        record.SynapseKind,
                        StringComparison.Ordinal)))
                {
                    latest = record;
                }
            }

            if (page.JournalEndPosition <= afterPosition
                || page.ReadThroughPosition <= afterPosition
                || page.ReadThroughPosition >= page.JournalEndPosition)
            {
                return latest;
            }

            afterPosition = page.ReadThroughPosition;
        }
    }

    private static T? Deserialize<T>(JsonElement serialization)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(serialization, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
