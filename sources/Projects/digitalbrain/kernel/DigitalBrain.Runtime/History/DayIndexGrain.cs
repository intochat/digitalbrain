using Orleans.Journaling;

namespace DigitalBrain.Runtime.History;

internal sealed class DayIndexGrain(
    [FromKeyedServices("day")] IDurableList<Guid> correlations)
    : DurableGrain, IDayIndex
{
    private HashSet<Guid> seen = new();

    public override Task OnActivateAsync(CancellationToken ct)
    {
        seen = correlations.ToHashSet();
        return base.OnActivateAsync(ct);
    }

    public async Task EnsureCorrelationAsync(Guid correlationId, CancellationToken ct)
    {
        if (seen.Add(correlationId))
        {
            correlations.Add(correlationId);
            await WriteStateAsync(ct);
        }
    }

    public Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Guid>>(correlations.ToArray());

    public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(correlations.Count);
}
