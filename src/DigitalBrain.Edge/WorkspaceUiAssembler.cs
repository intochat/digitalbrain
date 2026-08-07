namespace DigitalBrain.Edge;

/// <summary>
/// Combines independently journaled presentation surfaces into one bounded UI observation.
/// </summary>
public sealed class WorkspaceUiAssembler(IWorkspaceUiSurfaceSource source)
{
    public async Task<UiWorkspaceSnapshot> ReadAsync(
        IReadOnlyList<string> salesQueryIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(salesQueryIds);

        var surfaces = new List<UiSurface>();
        var approvals = await source.ReadApprovalsAsync(cancellationToken).ConfigureAwait(false);
        if (approvals is not null)
        {
            surfaces.Add(BaseUiKitAssembly.Approvals(approvals));
        }

        foreach (var queryId in salesQueryIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
            var sales = await source.ReadSalesAsync(queryId, cancellationToken).ConfigureAwait(false);
            if (sales is not null)
            {
                surfaces.Add(sales);
            }
        }

        var revision = surfaces.Count == 0 ? 0 : surfaces.Max(static surface => surface.Revision);
        return new UiWorkspaceSnapshot(revision, surfaces);
    }
}
