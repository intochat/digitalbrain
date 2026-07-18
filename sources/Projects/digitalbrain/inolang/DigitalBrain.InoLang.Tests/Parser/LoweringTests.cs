using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Tests;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class LoweringTests
{
    [Fact]
    public void Lowers_handlers_into_trigger_indexed_plan()
    {
        var bag = new DiagnosticBag();
        const string src = """
            neuron Acme.X
              using ask   = synapse(A.Req)
              using ready = signal(A.Done)
              on ask:
                emit ready(ok: "1")
            scenario "s"
              when synapse ask(text: "t")
              then signal ready emitted
            """;
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));

        var plan = Lowering.Lower(linked!);
        plan.Fqn.Should().Be("Acme.X");
        plan.HandlersFor(TriggerKey.Port("ask")).Should().ContainSingle();
        plan.Scenarios.Should().ContainSingle();
    }

    [Fact]
    public void Lowers_call_neurons_into_plan_Neurons_keyed_by_port_name()
    {
        // E-RUN #35: every `using port = neuron(Target)` becomes a NeuronBinding
        // the production neuron host can resolve to an Orleans grain reference.
        var bag = new DiagnosticBag();
        const string src = """
            neuron Acme.Probe
              using ask = synapse(A.Req)
              using gpt = neuron(A.GptNeuron)
              on ask:
                let r = ask gpt to "hi"
                log "{r}"

            scenario "s"
              given gpt returns "ack"
              when synapse ask(text: "t")
            """;
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));

        var plan = Lowering.Lower(linked!);

        plan.Neurons.Should().ContainKey("gpt");
        plan.Neurons["gpt"].TargetFqn.Should().Be("A.GptNeuron");
        plan.Neurons["gpt"].Key.Should().BeNull();
        plan.Neurons["gpt"].Sigil.Should().Be(PortSigil.Call);
    }

    [Fact]
    public void Lowers_resource_neurons_with_their_key_into_plan_Neurons()
    {
        // E-RUN #35: the `[\"key\"]` after a `port = neuron(Target)` carries
        // through to the grain reference's key — production host resolves the
        // keyed instance.
        var bag = new DiagnosticBag();
        const string src = """
            neuron Acme.Probe
              using ask = synapse(A.Req)
              using db  = neuron(A.Sqlite["analyses"])
              on ask:
                save "hello" into db

            scenario "s"
              when synapse ask(text: "t")
              then db has "hello"
            """;
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));

        var plan = Lowering.Lower(linked!);

        plan.Neurons.Should().ContainKey("db");
        plan.Neurons["db"].TargetFqn.Should().Be("A.Sqlite");
        plan.Neurons["db"].Key.Should().Be("analyses");
        plan.Neurons["db"].Sigil.Should().Be(PortSigil.Resource);
    }

    [Fact]
    public void Lowers_no_neurons_when_neuron_has_only_synapse_and_signal_ports()
    {
        var bag = new DiagnosticBag();
        const string src = """
            neuron Acme.Quiet
              using ask   = synapse(A.Req)
              using done  = signal(A.Done)
              on ask:
                emit done(ok: "1")
            scenario "s"
              when synapse ask(text: "t")
              then signal done emitted
            """;
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);

        var plan = Lowering.Lower(linked!);

        plan.Neurons.Should().BeEmpty();
    }
}
