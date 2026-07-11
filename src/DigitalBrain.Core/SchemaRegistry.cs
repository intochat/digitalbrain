namespace DigitalBrain.Core.Runtime;

public sealed record SchemaDescriptor(string Type, int Version, string Classification, bool ReplaySafe);

/// <summary>Authoritative discriminator/version registry. Unknown or conflicting schemas fail closed.</summary>
public sealed class SchemaRegistry
{
    private readonly Dictionary<(string Type, int Version), SchemaDescriptor> _schemas = new();

    public SchemaRegistry(IEnumerable<SchemaDescriptor>? descriptors = null)
    {
        foreach (var descriptor in descriptors ?? []) Register(descriptor);
    }

    public void Register(SchemaDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Type) || descriptor.Version < 1) throw new ArgumentException("A stable type and positive schema version are required.");
        var key = (descriptor.Type, descriptor.Version);
        if (_schemas.TryGetValue(key, out var existing) && existing != descriptor) throw new InvalidOperationException($"Conflicting schema registration: {descriptor.Type} v{descriptor.Version}.");
        _schemas[key] = descriptor;
    }

    public bool TryResolve(string type, int version, out SchemaDescriptor descriptor) => _schemas.TryGetValue((type, version), out descriptor!);
    public SchemaDescriptor Require(string type, int version) => TryResolve(type, version, out var descriptor) ? descriptor : throw new InvalidOperationException($"Unknown schema '{type}' v{version}.");
    public IReadOnlyList<SchemaDescriptor> Snapshot() => _schemas.Values.OrderBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.Version).ToArray();
}
