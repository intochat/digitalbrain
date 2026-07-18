namespace DigitalBrain.Runtime.Runtime;

// Sibling of ICallNeuronTarget (E-SDK #45) for neurons whose natural shape is a
// keyed resource rather than a single ask — the data domain's `~db` is the
// canonical example. Read returns the stored value (null when absent); Write
// stores a value at a key. Implementations interpret `key` per resource: a
// SQL row id, a document path, a KV slot. The InoLang `save into ~port`
// statement maps to Write; a future `let r = read ~port[...]` will map to
// Read once the grammar lands (deferred to E-INO).
//
// IGrainWithStringKey for the same reason as the sibling neurons: the InoLang
// `["key"]` is a string, and ProductionNeuronHost defaults the primary key to
// TargetFqn (singleton-per-type) when none is supplied.
public interface IResourceNeuronTarget : IGrainWithStringKey
{
    Task<string?> ReadAsync(string key, CancellationToken ct);
    Task WriteAsync(string key, string value, CancellationToken ct);
}
