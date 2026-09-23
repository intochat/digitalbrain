namespace DigitalBrain.Broker.Hosting;

public sealed record AssemblyLoadRequest
{
    public required string AssemblyName { get; init; }
    public string? PublicKeyToken { get; init; }
    public IReadOnlyList<string> ReferencedTypes { get; init; } = [];
}

public sealed record AssemblyLoadDecision
{
    public required bool Allowed { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

// A hosted silo loads only contracts assemblies, and only when they are signed and reference no
// banned API. Third-party implementations never become host assemblies. The verifier is pure so
// the load policy is testable without loading anything.
public sealed class AssemblyTrustVerifier(IEnumerable<string>? bannedApis = null)
{
    public static IReadOnlyList<string> DefaultBannedApis { get; } =
    [
        "System.IO.File",
        "System.IO.Directory",
        "System.Diagnostics.Process",
        "System.Reflection.Assembly",
        "System.Runtime.InteropServices.Marshal",
        "System.Runtime.Loader.AssemblyLoadContext",
        "System.Net.Http.HttpClient",
        "System.Net.Sockets.Socket",
    ];

    private readonly IReadOnlySet<string> bannedApis = new HashSet<string>(
        bannedApis ?? DefaultBannedApis, StringComparer.Ordinal);

    public AssemblyLoadDecision Verify(AssemblyLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reasons = new List<string>();
        if (!IsContractsAssembly(request.AssemblyName))
        {
            reasons.Add($"'{request.AssemblyName}' is not a contracts assembly; the host loads only '.Contracts' assemblies.");
        }
        if (string.IsNullOrWhiteSpace(request.PublicKeyToken))
        {
            reasons.Add($"'{request.AssemblyName}' is not signed.");
        }
        foreach (var type in request.ReferencedTypes)
        {
            if (bannedApis.Contains(type))
            {
                reasons.Add($"'{request.AssemblyName}' references banned API '{type}'.");
            }
        }
        return new AssemblyLoadDecision { Allowed = reasons.Count == 0, Reasons = reasons };
    }

    public IReadOnlyList<AssemblyLoadRequest> SelectLoadable(IEnumerable<AssemblyLoadRequest> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return [.. candidates.Where(candidate => Verify(candidate).Allowed)];
    }

    public static bool IsContractsAssembly(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        return assemblyName.Length > ".Contracts".Length
            && assemblyName.EndsWith(".Contracts", StringComparison.Ordinal);
    }
}
