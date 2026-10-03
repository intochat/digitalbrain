using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Registry;

internal sealed class RegistryDiscoveryService(IEnumerable<IRegistryResourceProvider> providers)
{
    public Task<RegistryDiscovery> Discover(string query, CancellationToken ct) => Browse(query, null, 0, 10, ct);

    private static void RequireCaller()
    {
        var caller = CallerContextStamper.Require();
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind == CallerKind.App)
        { throw new UnauthorizedAccessException("Only a trusted host may discover capabilities."); }
    }

    public async Task<RegistryDiscovery> Browse(string query, string? provider, int offset, int limit, CancellationToken ct)
    {
        RequireCaller();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 20);
        ct.ThrowIfCancellationRequested();
        if (provider is null)
        {
            // Rank module descriptions. Resource enumeration follows an explicit provider choice.
            var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var summaries = providers.Select(item => item.Summary)
                .OrderByDescending(item => terms.Count(term => (item.Id + " " + item.Description).Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(item => item.Id, StringComparer.Ordinal).Skip(offset).Take(limit + 1).ToArray();
            return new([.. summaries.Take(limit).Select(Describe)], [], summaries.Length > limit ? offset + limit : null);
        }
        var source = providers.SingleOrDefault(item => item.Id == provider);
        if (source is null) { return new([], [new(provider, "This provider is not available.")]); }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await source.Browse(offset, limit, budget.Token).WaitAsync(budget.Token);
            return result with { Capabilities = result.Capabilities.Take(limit).Select(Describe).ToArray() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return new([], [new(provider, "Capability resources are temporarily unavailable.")]); }
    }

    public async Task<RegistryCapability?> Select(string id, CancellationToken ct)
    {
        RequireCaller();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Length > 512) { throw new ArgumentException("Capability IDs must be bounded.", nameof(id)); }
        var provider = id.Split(':', 2)[0];
        var source = providers.SingleOrDefault(item => item.Id == provider);
        if (source is null) { return null; }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        return await source.Select(id, budget.Token).WaitAsync(budget.Token);
    }

    private static RegistryCapability Describe(RegistryCapability capability) => capability with { Tools = [], ResourcesJson = null };
}
