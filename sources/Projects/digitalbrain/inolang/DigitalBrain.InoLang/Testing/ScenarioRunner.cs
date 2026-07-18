using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Planning;
using DigitalBrain.InoLang.Runtime;

namespace DigitalBrain.InoLang.Testing;

public sealed class ScenarioRunner
{
    public async Task<ScenarioReport> RunAllAsync(ExecutionPlan plan, CancellationToken ct)
    {
        var results = new List<ScenarioResult>();
        foreach (var sc in plan.Scenarios)
            results.Add(await RunOneAsync(plan, sc, ct));
        return new ScenarioReport(results);
    }

    static async Task<ScenarioResult> RunOneAsync(
        ExecutionPlan plan, ScenarioDecl sc, CancellationToken ct)
    {
        var failures = new List<string>();
        var stub = new StubNeuronHost();

        // 1. given: pin every neuron / predicate
        foreach (var step in sc.Steps.OfType<GivenNeuronReturns>())
            stub.NeuronReturns[step.Port] = Literal(step.Value);
        foreach (var step in sc.Steps.OfType<GivenPredicate>())
            stub.PredicateValues[step.Subject.Builtin] = step.Value;

        // 2. when: inject the trigger synapse
        var when = sc.Steps.OfType<WhenInject>().FirstOrDefault();
        if (when is null)
        {
            failures.Add("scenario has no 'when' step");
            return new ScenarioResult(sc.Name, failures);
        }

        var inbound = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in when.Args)
            inbound[a.Name] = Literal(a.Value);
        var result = await new Interpreter(plan).RunAsync(
            TriggerKey.Port(when.Port), inbound, stub, ct);

        // 3. then/and: assert
        foreach (var step in sc.Steps)
            switch (step)
            {
                case ThenSynapseEmitted t:
                    var sig = result.EmittedSynapses.FirstOrDefault(e => e.Port == t.Port);
                    if (sig is null)
                        failures.Add($"expected synapse '!{t.Port}' was not emitted");
                    else if (t.WithField is { } f)
                    {
                        var exp = Literal(t.WithValue!);
                        var act = sig.Args.GetValueOrDefault(f, "<missing>");
                        if (act != exp)
                            failures.Add($"!{t.Port}.{f}: expected '{exp}' but was '{act}'");
                    }
                    break;
                case ThenResourceHas r:
                    var have = result.SavedResources.GetValueOrDefault(r.Port, "<none>");
                    var want = Literal(r.Value);
                    if (have != want)
                        failures.Add($"~{r.Port}: expected '{want}' but was '{have}'");
                    break;
                case ThenCounter c:
                    var cv = result.Counters.GetValueOrDefault(c.Counter, 0);
                    if (cv != c.Value)
                        failures.Add($"counter '{c.Counter}': expected {c.Value} but was {cv}");
                    break;
            }

        return new ScenarioResult(sc.Name, failures);
    }

    static string Literal(Expr e) => e switch
    {
        StringExpr s => s.Value,
        NumberExpr n => n.Value.ToString(),
        _ => ""
    };
}
