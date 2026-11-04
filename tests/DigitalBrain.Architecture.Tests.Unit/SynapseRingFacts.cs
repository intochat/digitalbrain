using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

// The synapse types live in the core: they reference no runtime and no test host.
public sealed partial class SynapseRingFacts
{
    [GeneratedRegex(@"\bDigitalBrain\s*\.\s*(Runtime|Testing)\b")]
    private static partial Regex Forbidden();

    [Fact]
    public void Core_synapse_types_reference_no_runtime_or_testing_namespace()
    {
        var core = Path.Combine(Repository.Root, "src", "DigitalBrain");
        var files = Repository.SourceFiles(Path.Combine(core, "Synapses"), "*.cs")
            .Concat([Path.Combine(core, "Spi", "ISynapseReceiver.cs"), Path.Combine(core, "Spi", "IGateIndex.cs")])
            .ToList();
        Assert.True(files.Count > 2);
        Assert.All(files, file => Assert.True(File.Exists(file), $"{file} is missing."));

        var hits = files.SelectMany(file => File.ReadLines(file).Select((line, index) => (file, index, line)))
            .Where(entry => Forbidden().IsMatch(entry.line))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.file)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }
}
