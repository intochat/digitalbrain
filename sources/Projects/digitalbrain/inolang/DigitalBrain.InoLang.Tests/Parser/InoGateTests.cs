using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class InoGateTests
{
    static ExecutionPlan Plan(string src)
    {
        var bag = new DiagnosticBag();
        var doc = new Parser(new Lexer(src, bag).Lex(), bag).ParseDocument();
        var cat = DeferredContractCatalog.Instance;
        var linked = new Linker(cat, bag).Link(doc!);
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return Lowering.Lower(linked!);
    }

    const string NoScenario = """
        neuron A.X
          using ask   = synapse(A.Req)
          using ready = signal(A.Done)
          on ask:
            emit ready(summary: "x")
        """;

    const string GreenScenario = NoScenario + """

        scenario "ok"
          when synapse ask(text: "t")
          then signal ready emitted with summary == "x"
        """;

    [Fact]
    public async Task Refuses_activation_when_no_scenarios()
    {
        var d = await InoGate.EvaluateAsync(Plan(NoScenario), CancellationToken.None);
        d.CanActivate.Should().BeFalse();
        d.Reason.Should().Contain("no scenario");
    }

    [Fact]
    public async Task Allows_activation_when_scenarios_green()
    {
        var d = await InoGate.EvaluateAsync(Plan(GreenScenario), CancellationToken.None);
        d.CanActivate.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_activation_when_scenario_red()
    {
        var plan = Plan(GreenScenario.Replace("summary == \"x\"", "summary == \"WRONG\""));
        var d = await InoGate.EvaluateAsync(plan, CancellationToken.None);
        d.CanActivate.Should().BeFalse();
        d.Reason.Should().Contain("red");
    }
}
