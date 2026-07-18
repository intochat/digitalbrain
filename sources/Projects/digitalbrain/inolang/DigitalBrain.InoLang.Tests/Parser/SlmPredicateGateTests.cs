namespace DigitalBrain.InoLang.Tests.Parsing;

// E-SDK #58 L6 gate exercise. Focused on the SLM-predicate path: a `.ino`
// whose only non-trivial scenario step is `given topic-of(#ask.text) is "X"`.
// The scenario projection runs through the in-process ScenarioRunner (via
// EvaluateGateAsync) and pins the StubNeuronHost; the gate goes green when the
// predicate-true path emits the expected signal, and red when the predicate
// pin no longer matches the .ino's `where ... is "..."` target.
//
// Companion to GoldenNorthStarTests: that file exercises the §4.1 worked
// example end-to-end; this one isolates the predicate so the test fails
// loudly if the bool ABI ever silently flips to "always false".
public sealed class SlmPredicateGateTests
{
    const string PredicateSource = """
        neuron Test.PredicateProbe
          using ask   = synapse(Test.Req)
          using gpt   = neuron(Test.Gpt)
          using ready = signal(Test.Done)
          @telemetry:counter:matches
          on ask where topic-of(ask.text) is "Car Insurance":
            let s = ask gpt to "summarize {ask.text}"
            count matches
            emit ready(summary: s)
        scenario "predicate-matching subject runs the body"
          given topic-of(ask.text) is "Car Insurance"
          given gpt returns "ok"
          when synapse ask(text: "my car insurance bill")
          then signal ready emitted with summary == "ok"
          and  counter matches == 1
        """;

    static IContractCatalog Catalog() => DeferredContractCatalog.Instance;

    [Fact]
    public async Task L6_gate_passes_when_given_predicate_pin_matches_the_where_target()
    {
        var compiled = InoCompiler.Compile(PredicateSource, Catalog());
        compiled.Success.Should().BeTrue(
            string.Join(";", compiled.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));

        var gate = await compiled.EvaluateGateAsync(CancellationToken.None);

        gate.CanActivate.Should().BeTrue(gate.Reason);
    }

    [Fact]
    public async Task L6_gate_refuses_when_given_predicate_pin_diverges_from_the_where_target()
    {
        // Drift only the `given` pin — keep the `where` target intact so the
        // .ino still compiles. The scenario then asserts the handler ran
        // (`then ... emitted`, `counter matches == 1`) but the StubNeuronHost
        // returns false for the predicate, the handler never runs, and the
        // gate goes red.
        var drifted = PredicateSource.Replace(
            "given topic-of(ask.text) is \"Car Insurance\"",
            "given topic-of(ask.text) is \"Personal Finance\"");
        var compiled = InoCompiler.Compile(drifted, Catalog());
        compiled.Success.Should().BeTrue();

        var gate = await compiled.EvaluateGateAsync(CancellationToken.None);

        gate.CanActivate.Should().BeFalse();
        // "red" alone is the gate's generic refusal wording — also assert
        // the specific failure surface (the ready signal was never emitted)
        // so a future re-wording of the gate's reason format does not let
        // this test pass for the wrong reason.
        gate.Reason.Should().Contain("red").And.Contain("ready");
    }
}
