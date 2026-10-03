using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Registry;

internal sealed class RegistryDiscoveryService(IEnumerable<IRegistryResourceProvider> providers)
{
    public async Task<RegistryDiscovery> Discover(string query, CancellationToken cancellationToken)
    {
        var caller = CallerContextStamper.Require();
        if (!CallerContextStamper.IsTrusted(caller) || caller.Kind == CallerKind.App)
        { throw new UnauthorizedAccessException("Only a trusted host may discover capabilities."); }
        var capabilities = new List<RegistryCapability>();
        var errors = new List<RegistryDiscoveryError>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await provider.Discover(cancellationToken);
                capabilities.AddRange(result.Capabilities);
                errors.AddRange(result.Errors);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            { errors.Add(new(provider.Id, "Capability resources are temporarily unavailable.")); }
        }
        // Query ranks the complete catalog; typos and missing semantic services never hide tools.
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new([.. capabilities.OrderByDescending(capability => terms.Count(term =>
            (capability.Id + " " + capability.Description).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(capability => capability.Id, StringComparer.Ordinal)], [.. errors]);
    }
}
