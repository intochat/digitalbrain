using System.Linq;
using DigitalBrain.InoLang.Domain.Ino;
using DigitalBrain.InoLang.Domain.Yaml;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

public class LanguageLeafTests
{
    private const string SampleIno =
        "name: memory\n" +
        "version: 1.0.0\n" +
        "desc: Memory\n" +
        "triggers: MemoryRecall\n" +
        "emits: MemoryRecallSynapse,UiSurface\n" +
        "observed-synapses: 0\n" +
        "\n" +
        "on: MemoryRecall\n" +
        "  show card( \"Memory $key\", column( text( \"$value\" ) ) )\n";

    private const string SampleYaml =
        "schemaVersion: \"os-on-yaml/v0\"\n" +
        "neuron:\n" +
        "  id: memory\n" +
        "  grainType: memory\n" +
        "  version: 1.0.0\n" +
        "  desc: Memory experience\n" +
        "  emits:\n" +
        "    - UiSurface\n" +
        "  observedSynapses: 0\n" +
        "  rules:\n" +
        "    - on: RememberSynapse\n" +
        "      do:\n" +
        "        - show:\n" +
        "            card:\n" +
        "              title: \"Remembered\"\n";

    [Fact]
    public void InoLang_assembly_does_not_reference_Core()
    {
        var asm = typeof(InoParser).Assembly;
        Assert.Equal("DigitalBrain.InoLang", asm.GetName().Name);
        Assert.DoesNotContain("DigitalBrain.Core", asm.GetReferencedAssemblies().Select(a => a.Name));
    }

    [Fact]
    public void InoParser_parses_name_emits_and_rule()
    {
        var exp = InoParser.Parse(SampleIno);

        Assert.Equal("memory", exp.Name);
        Assert.Equal("1.0.0", exp.Version);
        Assert.Contains("MemoryRecallSynapse", exp.Emits);
        Assert.Contains(exp.Rules, r => r.On == "MemoryRecall");
    }

    [Fact]
    public void YamlParser_parses_neuron_id_and_rule()
    {
        var exp = YamlParser.Parse(SampleYaml);

        Assert.NotNull(exp);
        Assert.Equal("memory", exp!.Name);
        Assert.Contains(exp.Rules, r => r.On == "RememberSynapse");
    }
}
