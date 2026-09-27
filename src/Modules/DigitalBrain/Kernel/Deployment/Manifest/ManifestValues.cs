using System.Text.RegularExpressions;
using Pulumi;

namespace DigitalBrain.Deployment;

// Turns manifest expressions like "Endpoint={qdrant.bindings.grpc.url};Key={qdrant-Key.value}" into
// Pulumi outputs. Parameters come from stack config; everything a resource exposes (bindings, outputs,
// connection strings) must be provided by whoever deploys that resource.
public sealed partial class ManifestValues(AspireManifest manifest, Func<string, Output<string>> parameter)
{
    private readonly Dictionary<string, Output<string>> _provided = new(StringComparer.Ordinal);
    private readonly HashSet<string> _secretProvided = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Missing => _missing;

    public void Provide(string resource, string path, Input<string> value, bool secret = false)
    {
        _provided[resource + "." + path] = secret ? Output.CreateSecret(value.ToOutput()) : value.ToOutput();
        if (secret) { _secretProvided.Add(resource + "." + path); }
    }

    public bool IsProvided(string resource, string path) => _provided.ContainsKey(resource + "." + path);

    // Secret when any parameter it reads is secret, so composites like connection strings stay secret.
    public bool IsSecret(string expression) => References(expression).Any(reference
        => _secretProvided.Contains(reference.Resource + "." + reference.Path) || Reads(reference.Resource).Any(manifest.IsSecretParameter));

    public Output<string> Resolve(string expression)
    {
        var parts = new List<Output<string>>();
        var last = 0;
        foreach (Match match in Reference().Matches(expression))
        {
            if (match.Index > last) { parts.Add(Output.Create(expression[last..match.Index])); }
            parts.Add(ResolveReference(match.Groups["resource"].Value, match.Groups["path"].Value));
            last = match.Index + match.Length;
        }
        if (last < expression.Length) { parts.Add(Output.Create(expression[last..])); }
        return parts.Count == 0 ? Output.Create("") : Output.All(parts).Apply(values => string.Concat(values));
    }

    private Output<string> ResolveReference(string resource, string path)
    {
        if (_provided.TryGetValue(resource + "." + path, out var provided)) { return provided; }
        if (!manifest.ResourceNames.Contains(resource))
        {
            _missing.Add($"{resource}.{path}");
            return Output.Create("");
        }
        var definition = manifest.Resource(resource);
        switch (manifest.TypeOf(resource), path)
        {
            case ("parameter.v0", "value"):
                return parameter(resource);
            case ("annotated.string", "value"):
                var annotated = Resolve(definition.GetProperty("value").GetString()!);
                return definition.TryGetProperty("filter", out var filter) && filter.GetString() == "uri"
                    ? annotated.Apply(Uri.EscapeDataString)
                    : annotated;
            case ("value.v0" or "container.v0", "connectionString") when definition.TryGetProperty("connectionString", out var connection):
                return Resolve(connection.GetString()!);
            default:
                _missing.Add($"{resource}.{path}");
                return Output.Create("");
        }
    }

    // The parameters an expression reads, through value and annotated resources.
    private IEnumerable<string> Reads(string resource)
    {
        if (!manifest.ResourceNames.Contains(resource)) { return []; }
        var definition = manifest.Resource(resource);
        return manifest.TypeOf(resource) switch
        {
            "parameter.v0" => [resource],
            "annotated.string" => References(definition.GetProperty("value").GetString()!).SelectMany(reference => Reads(reference.Resource)),
            "value.v0" or "container.v0" when definition.TryGetProperty("connectionString", out var connection)
                => References(connection.GetString()!).Where(reference => reference.Resource != resource).SelectMany(reference => Reads(reference.Resource)),
            _ => [],
        };
    }

    private static IEnumerable<(string Resource, string Path)> References(string expression)
        => Reference().Matches(expression).Select(match => (match.Groups["resource"].Value, match.Groups["path"].Value));

    [GeneratedRegex(@"\{(?<resource>[A-Za-z0-9_-]+)\.(?<path>[A-Za-z0-9_.-]+)\}")]
    private static partial Regex Reference();
}
