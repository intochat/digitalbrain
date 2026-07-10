namespace DigitalBrain.Core.V2;

public sealed record V2SchemaDescriptor(string Type, int Version, string Classification, bool ReplaySafe);

/// <summary>Authoritative V2 discriminator/version registry. Unknown or conflicting schemas fail closed.</summary>
public sealed class V2SchemaRegistry
{
    private readonly Dictionary<(string Type, int Version), V2SchemaDescriptor> _schemas = new();

    public V2SchemaRegistry(IEnumerable<V2SchemaDescriptor>? descriptors = null)
    {
        foreach (var descriptor in descriptors ?? []) Register(descriptor);
    }

    public void Register(V2SchemaDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Type) || descriptor.Version < 1) throw new ArgumentException("A stable V2 type and positive schema version are required.");
        var key = (descriptor.Type, descriptor.Version);
        if (_schemas.TryGetValue(key, out var existing) && existing != descriptor) throw new InvalidOperationException($"Conflicting V2 schema registration: {descriptor.Type} v{descriptor.Version}.");
        _schemas[key] = descriptor;
    }

    public bool TryResolve(string type, int version, out V2SchemaDescriptor descriptor) => _schemas.TryGetValue((type, version), out descriptor!);
    public V2SchemaDescriptor Require(string type, int version) => TryResolve(type, version, out var descriptor) ? descriptor : throw new InvalidOperationException($"Unknown V2 schema '{type}' v{version}.");
    public IReadOnlyList<V2SchemaDescriptor> Snapshot() => _schemas.Values.OrderBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.Version).ToArray();
}
