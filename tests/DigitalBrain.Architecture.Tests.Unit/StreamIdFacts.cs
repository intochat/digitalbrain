using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

// 04 D-06, D-12: stream ids are persisted (PubSubStore, queues), so the binding name, the namespace prefix and the
// stream item alias are pinned. Changing one in source without updating ContractManifest.json fails here.
// Read from source text: this project does not reference the runtime.
public sealed partial class StreamIdFacts
{
    private static JsonElement Streams()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, "tests", "DigitalBrain.Architecture.Tests.Unit", "ContractManifest.json")));
        return manifest.RootElement.GetProperty("streams").Clone();
    }

    private static string Constant(string relative, string name)
    {
        var text = File.ReadAllText(Path.Combine(Repository.Root, "src", "DigitalBrain.Runtime.Orleans", relative));
        var match = Regex.Match(text, $@"const\s+string\s+{name}\s*=\s*""(?<value>[^""]*)""");
        Assert.True(match.Success, $"{name} not found in {relative}.");
        return match.Groups["value"].Value;
    }

    [Fact]
    public void The_signal_stream_binding_name_is_pinned()
        => Assert.Equal(Streams().GetProperty("binding").GetString(), Constant("IntoChatOSNames.cs", "SignalStreams"));

    [Fact]
    public void The_stream_namespace_prefix_is_pinned()
    {
        Assert.Equal("brain.signals.", Streams().GetProperty("namespacePrefix").GetString());
        Assert.Equal(Streams().GetProperty("namespacePrefix").GetString(), Constant(Path.Combine("Synapses", "StreamNames.cs"), "NamespacePrefix"));
    }

    [Fact]
    public void The_stream_item_alias_is_pinned()
        => Assert.Equal(Streams().GetProperty("item").GetString(), Constant(Path.Combine("Synapses", "SignalReference.cs"), "WireAlias"));
}
