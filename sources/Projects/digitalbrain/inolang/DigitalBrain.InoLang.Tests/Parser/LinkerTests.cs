using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Linking;
using Xunit;
using FluentAssertions;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class LinkerTests
{
    static IContractCatalog Catalog() => DeferredContractCatalog.Instance;

    static (LinkedNeuron?, DiagnosticBag) Link(string s)
    {
        var bag = new DiagnosticBag();
        var doc = new Parser(new Lexer(s, bag).Lex(), bag).ParseDocument();
        var linked = new Linker(Catalog(), bag).Link(doc!);
        return (linked, bag);
    }

    const string Ok = """
        neuron Acme.X
          using ask   = synapse(DigitalBrain.User.Request)
          using gpt   = neuron(DigitalBrain.SDK.ChatGpt)
          using ready = signal(Acme.AnalysisReady)
          on ask:
            let s = ask gpt to "x {ask.text}"
            emit ready(summary: s)
        scenario "s"
          given gpt returns "y"
          when synapse ask(text: "t")
          then signal ready emitted with summary == "y"
        """;

    [Fact]
    public void Valid_document_links_clean()
    {
        var (linked, bag) = Link(Ok);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        linked.Should().NotBeNull();
        linked!.Ports.Should().ContainKey("ask");
    }

    [Fact]
    public void Field_access_on_undeclared_port_is_a_link_error()
    {
        const string src = """
            neuron Acme.X
              using ask   = synapse(DigitalBrain.User.Request)
              using ready = signal(Acme.AnalysisReady)
              on ask:
                log "v: {ghost.text}"
                emit ready(summary: "ok")
            scenario "s"
              when synapse ask(text: "x")
              then signal ready emitted
            """;
        var (linked, bag) = Link(src);
        bag.Items.Should().Contain(d => d.Code == "INO305");
        linked.Should().BeNull();
    }

    [Fact]
    public void Scenario_asserting_undeclared_counter_is_a_link_error()
    {
        const string src = """
            neuron Acme.X
              using ask   = synapse(DigitalBrain.User.Request)
              using ready = signal(Acme.AnalysisReady)
              on ask:
                emit ready(summary: "x")
            scenario "s"
              when synapse ask(text: "t")
              then signal ready emitted
              and counter ghost == 0
            """;
        var (linked, bag) = Link(src);
        bag.Items.Should().Contain(d => d.Code == "INO310");
        linked.Should().BeNull();
    }
}
