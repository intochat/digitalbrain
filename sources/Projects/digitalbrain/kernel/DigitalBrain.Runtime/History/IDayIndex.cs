namespace DigitalBrain.Runtime.History;

public interface IDayIndex : IGrainWithStringKey
{
    Task EnsureCorrelationAsync(Guid correlationId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ListAsync(CancellationToken ct);
    Task<int> CountAsync(CancellationToken ct);
}
