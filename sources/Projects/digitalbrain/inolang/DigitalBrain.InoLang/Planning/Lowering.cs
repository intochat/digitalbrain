using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.InoLang.Planning;

public static class Lowering
{
    public static ExecutionPlan Lower(LinkedNeuron linked)
    {
        var doc = linked.Doc;
        var ports = linked.Ports;
        var handlers = doc.Handlers.Select(h => new PlannedHandler(
            h.Trigger switch
            {
                PortTrigger p => TriggerKey.Port(p.Port),
                BroadcastTrigger b => TriggerKey.Broadcast(b.Fqn),
                LifecycleTrigger l => TriggerKey.Lifecycle(l.Name),
                FailureTrigger f => TriggerKey.Failure(f.Branch),
                _ => TriggerKey.Lifecycle("unknown")
            },
            h.Where is { } w ? CanonicalizePredicate(w, ports) : null,
            CanonicalizeStmts(h.Body, ports))).ToList();

        // PortKind.Neuron is the only Kind that carries a grain-bound target —
        // synapses route through the cortex/bus instead. Sigil (Call vs Resource)
        // is what the runtime neuron host uses to discriminate ask $x vs save into
        // ~x; preserve it here so the host doesn't have to re-parse the source.
        var neurons = ports.Values
            .Where(port => port.Decl.Kind == PortKind.Neuron)
            .ToDictionary(
                port => port.Decl.Name,
                port => new NeuronBinding(port.Decl.Sigil, port.Decl.TargetFqn, port.Decl.Key),
                StringComparer.Ordinal);

        // v5 C2: `using !x = synapse(T)` declares an emitter port for which
        // `emit !x(...)` records `EmittedSynapse.Port = "x"`. The Cortex needs
        // "x" → T (the broadcast FQN) at fan-out time; capturing it on the plan
        // keeps the InoLang ABI's EmittedSynapse shape unchanged.
        var synapsePorts = ports.Values
            .Where(port => port.Decl.Kind == PortKind.Synapse)
            .ToDictionary(
                port => port.Decl.Name,
                port => port.Decl.TargetFqn,
                StringComparer.Ordinal);

        var scenarios = doc.Scenarios
            .Select(sc => sc with { Steps = CanonicalizeSteps(sc.Steps, ports) })
            .ToList();

        return new ExecutionPlan(doc.Fqn, handlers, scenarios, doc.Counters, neurons, synapsePorts, doc.Ui);
    }

    // E-INO #74. The Linker (#43/#73) accepts either casing against
    // ContractSchema.Fields, but Interpreter walks the AST with ordinal dict
    // lookups; a green link could silently produce "" when the author's
    // casing diverged from the schema or from itself across when/handler/then.
    // Lowering is the right cut point — the LinkedPort.Schema is already in
    // hand — so we rewrite every name bound to a schema field to the schema's
    // canonical casing. The runtime then sees consistent keys without any
    // case-insensitive relaxation in hot paths.
    static IReadOnlyList<Stmt> CanonicalizeStmts(
        IReadOnlyList<Stmt> stmts,
        IReadOnlyDictionary<string, LinkedPort> ports)
        => [.. stmts.Select(s => CanonicalizeStmt(s, ports))];

    static Stmt CanonicalizeStmt(Stmt s, IReadOnlyDictionary<string, LinkedPort> ports) => s switch
    {
        LetAskStmt l => l with { Prompt = CanonicalizeExpr(l.Prompt, ports) },
        LetExprStmt le => le with { Value = CanonicalizeExpr(le.Value, ports) },
        EmitStmt e => e with { Args = CanonicalizeArgs(e.Args, e.Port, ports) },
        SaveStmt sv => sv with { Value = CanonicalizeExpr(sv.Value, ports) },
        LogStmt lg => lg with { Message = CanonicalizeExpr(lg.Message, ports) },
        IfStmt i => i with
        {
            Cond = CanonicalizeExpr(i.Cond, ports),
            ThenBody = [.. i.ThenBody.Select(b => CanonicalizeStmt(b, ports))],
            ElseBody = [.. i.ElseBody.Select(b => CanonicalizeStmt(b, ports))]
        },
        ForEachStmt f => f with
        {
            SourceList = CanonicalizeExpr(f.SourceList, ports),
            Body = [.. f.Body.Select(b => CanonicalizeStmt(b, ports))]
        },
        SpeculateStmt spec => spec with
        {
            Body = [.. spec.Body.Select(b => CanonicalizeStmt(b, ports))]
        },
        VerifyStmt v => v with { Cond = CanonicalizeExpr(v.Cond, ports) },
        FlowMappingStmt fm => fm with
        {
            Source = CanonicalizeExpr(fm.Source, ports),
            Target = CanonicalizeExpr(fm.Target, ports)
        },
        WriteStmt w => w with
        {
            Target = CanonicalizeExpr(w.Target, ports),
            Value = CanonicalizeExpr(w.Value, ports)
        },
        _ => s
    };

    static Predicate CanonicalizePredicate(Predicate w, IReadOnlyDictionary<string, LinkedPort> ports)
        => w with { Subject = (CallExpr)CanonicalizeExpr(w.Subject, ports) };

    static Expr CanonicalizeExpr(Expr e, IReadOnlyDictionary<string, LinkedPort> ports) => e switch
    {
        FieldAccessExpr f => f with { Field = CanonicalField(f.Field, f.PortName, ports) },
        CallExpr c => c with { Arg = CanonicalizeExpr(c.Arg, ports) },
        ArgsExpr a => a with { Args = [.. a.Args.Select(arg => arg with { Value = CanonicalizeExpr(arg.Value, ports) })] },
        InterpExpr i => i with { Parts = [.. i.Parts.Select(p => CanonicalizeExpr(p, ports))] },
        _ => e
    };

    static IReadOnlyList<ScenarioStep> CanonicalizeSteps(
        IReadOnlyList<ScenarioStep> steps,
        IReadOnlyDictionary<string, LinkedPort> ports)
        => [.. steps.Select(st => CanonicalizeStep(st, ports))];

    static ScenarioStep CanonicalizeStep(ScenarioStep st, IReadOnlyDictionary<string, LinkedPort> ports) => st switch
    {
        GivenNeuronReturns g => g with { Value = CanonicalizeExpr(g.Value, ports) },
        GivenPredicate gp => gp with { Subject = (CallExpr)CanonicalizeExpr(gp.Subject, ports) },
        WhenInject w => w with { Args = CanonicalizeArgs(w.Args, w.Port, ports) },
        ThenSynapseEmitted t => t with
        {
            WithField = t.WithField is null ? null : CanonicalField(t.WithField, t.Port, ports),
            WithValue = t.WithValue is null ? null : CanonicalizeExpr(t.WithValue, ports)
        },
        ThenResourceHas r => r with { Value = CanonicalizeExpr(r.Value, ports) },
        _ => st
    };

    static IReadOnlyList<NamedArg> CanonicalizeArgs(
        IReadOnlyList<NamedArg> args,
        string portName,
        IReadOnlyDictionary<string, LinkedPort> ports)
        => [.. args.Select(a => new NamedArg(
            CanonicalField(a.Name, portName, ports),
            CanonicalizeExpr(a.Value, ports)))];

    static string CanonicalField(
        string authored,
        string portName,
        IReadOnlyDictionary<string, LinkedPort> ports)
    {
        if (!ports.TryGetValue(portName, out var port)) return authored;
        foreach (var field in port.Schema.Fields)
            if (StringComparer.OrdinalIgnoreCase.Equals(field, authored))
                return field;
        return authored;
    }

}
