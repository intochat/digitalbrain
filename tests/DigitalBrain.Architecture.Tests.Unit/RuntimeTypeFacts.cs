using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

// 04 D-06, 06 specifics (keys never move), T-04-50, T-04-52: grain types, serializer and interface aliases, runtime neuron types
// and the framework's synapse signals are persisted (grain ids, PubSubStore, journal entries), so every one of them is pinned
// in ContractManifest.json. Adding or renaming one in source without updating the manifest fails here and names the difference.
// Read from source text: this project does not reference the runtime.
public sealed partial class RuntimeTypeFacts
{
    private static readonly string[] Scanned = ["DigitalBrain", "DigitalBrain.Runtime.Orleans"];

    // The names this phase added (04-16 must-haves); the namespace prefix is pinned under "streams".
    private static readonly string[] PhaseNames =
    [
        "neuron.host", "neuron.host.reentrant", "neuron.host.stateless", "brain.synapse-cursors", "brain.synapse-index",
        "runtime.session", "runtime.listener", "synapse.ref", "synapse.gap",
    ];

    private static readonly string[] Kinds = ["grainTypes", "aliases", "neurons", "signals"];

    private static JsonElement Manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "tests", "DigitalBrain.Architecture.Tests.Unit", "ContractManifest.json")));
        return manifest.RootElement.Clone();
    }

    private static SortedSet<string> Pinned(string kind)
        => new(Manifest().GetProperty("runtime").GetProperty(kind).EnumerateArray().Select(item => item.GetString()!), StringComparer.Ordinal);

    private static IEnumerable<string> Files()
        => Scanned.SelectMany(name => Repository.SourceFiles(Path.Combine(Repository.Root, "src", name), "*.cs"));

    // Source without comment lines: doc text such as <c>[Signal(alias)]</c> is not a declaration.
    private static string Code(string path)
        => string.Join('\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    // const string Name = "value", keyed by bare name and by Type.Name of the type declared before it.
    private static Dictionary<string, List<string>> Constants()
    {
        var constants = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var text in Files().Select(Code))
        {
            foreach (Match match in ConstantPattern().Matches(text))
            {
                var name = match.Groups["name"].Value;
                var value = match.Groups["value"].Value;
                var owner = TypePattern().Matches(text[..match.Index]).LastOrDefault()?.Groups["type"].Value;
                Add(constants, name, value);
                if (owner is not null) { Add(constants, owner + "." + name, value); }
            }
        }
        return constants;

        static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var values)) { map[key] = values = []; }
            if (!values.Contains(value)) { values.Add(value); }
        }
    }

    private static SortedSet<string> Captured(string kind)
    {
        var constants = Constants();
        var pattern = kind switch
        {
            "grainTypes" => GrainTypePattern(),
            "aliases" => AliasPattern(),
            "neurons" => NeuronPattern(),
            _ => SignalPattern(),
        };
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Files())
        {
            foreach (Match match in pattern.Matches(Code(path)))
            {
                if (match.Groups["literal"].Success) { names.Add(match.Groups["literal"].Value); continue; }
                var reference = match.Groups["constant"].Value;
                Assert.True(constants.TryGetValue(reference, out var values) && values.Count == 1,
                    $"{Path.GetFileName(path)}: the constant {reference} does not resolve to one string.");
                names.Add(values![0]);
            }
        }
        // Runtime neuron types and the framework's own signals are the ones pinned; module neurons belong to modules.
        return kind switch
        {
            "neurons" => new(names.Where(name => name.StartsWith("runtime.", StringComparison.Ordinal)), StringComparer.Ordinal),
            "signals" => new(names.Where(name => name.StartsWith("synapse.", StringComparison.Ordinal)), StringComparer.Ordinal),
            _ => names,
        };
    }

    [Fact]
    public void Every_runtime_grain_type_alias_neuron_type_and_synapse_signal_is_pinned_in_ContractManifest()
    {
        var differences = new List<string>();
        foreach (var kind in Kinds)
        {
            var source = Captured(kind);
            var pinned = Pinned(kind);
            differences.AddRange(source.Except(pinned).Select(name => $"{kind}: '{name}' is in source but not in ContractManifest.json"));
            differences.AddRange(pinned.Except(source).Select(name => $"{kind}: '{name}' is in ContractManifest.json but not in source (renamed or removed)"));
        }
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void The_manifest_holds_every_runtime_name_of_phase_4()
    {
        var pinned = Kinds.SelectMany(Pinned).ToHashSet(StringComparer.Ordinal);
        var missing = PhaseNames.Where(name => !pinned.Contains(name)).ToList();
        Assert.True(missing.Count == 0, "Not pinned: " + string.Join(", ", missing));
        Assert.Equal("brain.signals.", Manifest().GetProperty("streams").GetProperty("namespacePrefix").GetString());
    }

    [Fact]
    public void No_topology_class_registers_the_test_hooks()
    {
        var topology = Repository.SourceFiles(Path.Combine(Repository.Root, "src", "DigitalBrain.Runtime.Orleans", "Topology"), "*.cs")
            .Concat(Repository.SourceFiles(Path.Combine(Repository.Root, "src", "DigitalBrain.Runtime.Orleans.Azure"), "*.cs"))
            .ToList();
        Assert.NotEmpty(topology);
        var offenders = topology.Where(path => HookPattern().IsMatch(File.ReadAllText(path))).Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"(?<=[\[,]\s*)GrainType\(\s*(?:""(?<literal>[^""]+)""|(?<constant>[A-Za-z_][\w.]*))\s*\)")]
    private static partial Regex GrainTypePattern();

    [GeneratedRegex(@"(?<=[\[,]\s*)Alias\(\s*(?:""(?<literal>[^""]+)""|(?<constant>[A-Za-z_][\w.]*))\s*\)")]
    private static partial Regex AliasPattern();

    [GeneratedRegex(@"\[\s*Neuron\(\s*(?:""(?<literal>[^""]+)""|(?<constant>[A-Za-z_][\w.]*))")]
    private static partial Regex NeuronPattern();

    [GeneratedRegex(@"\[\s*Signal\(\s*(?:""(?<literal>[^""]+)""|(?<constant>[A-Za-z_][\w.]*))")]
    private static partial Regex SignalPattern();

    [GeneratedRegex(@"const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex ConstantPattern();

    [GeneratedRegex(@"\b(?:class|interface|record|struct)\s+(?<type>\w+)")]
    private static partial Regex TypePattern();

    [GeneratedRegex(@"\b(?:PublishFaults|RuntimeTestHooks)\b")]
    private static partial Regex HookPattern();
}
