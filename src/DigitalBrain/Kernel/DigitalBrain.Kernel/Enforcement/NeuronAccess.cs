namespace DigitalBrain.Kernel.Enforcement;

// Supplied by the receiving module, never by a caller or a discovery result.
public sealed record NeuronAccess(string? Scope = null, bool Public = false)
{
    public static NeuronAccess Unclassified { get; } = new();
    public static NeuronAccess PublicOperation { get; } = new(Public: true);

    // The reserved workspace namespace is an address-level tenant boundary. Keys compose it
    // with '/' for path-shaped neurons and ':' for UI-part names; either way the first segment
    // is the scope. Modules with aliases, persisted owners or public operations override it.
    public static NeuronAccess ForBrainKey(string key)
    {
        var separator = key.AsSpan().IndexOfAny('/', ':');
        var scope = separator < 0 ? key : key[..separator];
        return scope.Length == 74 && scope.StartsWith("workspace-", StringComparison.Ordinal)
            && !scope.AsSpan(10).ContainsAnyExcept("0123456789abcdef") ? new(scope) : Unclassified;
    }
}
