namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class GoldenNorthStarTests
{
    // The exact §4.1 document from docs/v3/VISION.md (comments included).
    const string NorthStar = """
        neuron Acme.BusinessAnalyst
          "Becomes a domain-expert business analyst for a chat app."        # intent line

          using ask   = synapse(DigitalBrain.User.Request)                 # inbound port
          using gpt   = neuron(DigitalBrain.SDK.ChatGpt)                   # AI neuron
          using db    = neuron(DigitalBrain.Data.Sqlite["analysis"])       # stateful neuron
          using ready = signal(Acme.AnalysisReady)                         # outbound signal

          @telemetry:counter:analyses_completed

          on activated:                                                     # component hook
            log "spawned"

          on ask where topic-of(ask.text) is "Car Insurance":            # fuzzy predicate
            let summary = ask gpt to "analyze {ask.text}"
            save summary into db
            count analyses_completed
            emit ready(summary: summary)

        scenario "produces an analysis for an insurance prompt"
          given topic-of(ask.text) is "Car Insurance"                      # pin the SLM neuron
          given gpt returns "summary: crowded market"
          when  synapse ask(text: "I run a car insurance startup")
          then  db has "summary: crowded market"
          and   signal ready emitted with summary == "summary: crowded market"
          and   counter analyses_completed == 1
        """;

    static IContractCatalog Catalog() => DeferredContractCatalog.Instance;

    [Fact]
    public async Task North_star_compiles_links_and_gates_green()
    {
        var compiled = InoCompiler.Compile(NorthStar, Catalog());
        compiled.Success.Should().BeTrue(
            string.Join(";", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

        var gate = await compiled.EvaluateGateAsync(CancellationToken.None);
        gate.CanActivate.Should().BeTrue(gate.Reason);
    }

    [Fact]
    public async Task Breaking_the_spec_makes_the_gate_refuse()
    {
        // Controller correction of a latent plan bug: the original plan used
        // .Replace("crowded market","calm market"), which rewrites BOTH the
        // stubbed $gpt neuron return AND the then-assertions identically — the
        // scenario would stay GREEN and this test's intent would be void.
        // Break ONLY an assertion instead (counter expected 1 -> 2), so the
        // produced value no longer matches: the scenario goes genuinely RED
        // while compilation still succeeds.
        var compiled = InoCompiler.Compile(
            NorthStar.Replace("analyses_completed == 1", "analyses_completed == 2"),
            Catalog());
        compiled.Success.Should().BeTrue();
        var gate = await compiled.EvaluateGateAsync(CancellationToken.None);
        gate.CanActivate.Should().BeFalse();
        gate.Reason.Should().Contain("red");
    }
}
