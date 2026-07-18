using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Diagnostics;

namespace DigitalBrain.InoLang.Linking;

public sealed class Linker(IContractCatalog catalog, DiagnosticBag diagnostics)
{
    private NeuronDoc? _doc;

    public LinkedNeuron? Link(NeuronDoc? doc)
    {
        if (doc is null) return null;
        _doc = doc;

        var ports = new Dictionary<string, LinkedPort>(StringComparer.Ordinal);
        foreach (var u in doc.Usings)
        {
            var schema = catalog.Resolve(u.TargetFqn);
            if (schema is null)
            {
                diagnostics.Error("INO300",
                    $"Unknown contract '{u.TargetFqn}' for port '{u.Name}'.", u.Span);
                continue;
            }
            ports[u.Name] = new LinkedPort(u, schema);
        }

        foreach (var h in doc.Handlers)
        {
            var localPorts = new Dictionary<string, LinkedPort>(ports, StringComparer.Ordinal);
            ContractSchema? triggerSchema = null;
            if (h.Trigger is PortTrigger pt)
            {
                var ptPort = pt.Port;
                var ptSchema = catalog.Resolve(ptPort);
                if (ptSchema is null && StandardAliases.TryGetValue(ptPort, out var alias))
                {
                    ptSchema = catalog.Resolve(alias);
                }
                if (ptSchema is null && ports.TryGetValue(ptPort, out var lp))
                {
                    ptSchema = lp.Schema;
                }
                triggerSchema = ptSchema;
            }
            else if (h.Trigger is BroadcastTrigger bt)
            {
                triggerSchema = catalog.Resolve(bt.Fqn);
            }

            if (triggerSchema is not null)
            {
                var triggerPort = new LinkedPort(
                    new UsingDecl(PortSigil.Call, "it",
                        triggerSchema.Kind switch {
                            ContractKind.Synapse => PortKind.Synapse,
                            _ => PortKind.Neuron
                        },
                        triggerSchema.Fqn, null, h.Span),
                    triggerSchema
                );
                localPorts["it"] = triggerPort;
                localPorts[triggerSchema.Fqn] = triggerPort;
                var lastDot = triggerSchema.Fqn.LastIndexOf('.');
                var shortName = lastDot >= 0 ? triggerSchema.Fqn[(lastDot + 1)..] : triggerSchema.Fqn;
                localPorts[shortName] = triggerPort;
            }

            if (h.Where is { } w) CheckExpr(w.Subject, localPorts, doc);
            foreach (var s in h.Body) CheckStmt(s, localPorts, doc);
        }
        foreach (var sc in doc.Scenarios)
        foreach (var st in sc.Steps)
            CheckScenarioStep(st, ports, doc);

        return diagnostics.HasErrors ? null : new LinkedNeuron(doc, ports);
    }

    public static readonly Dictionary<string, string> StandardAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UserRequest"] = "DigitalBrain.User.Request",
        ["GPT"] = "DigitalBrain.SDK.ChatGpt",
        ["Database"] = "DigitalBrain.Data.Sqlite",
        ["db"] = "DigitalBrain.Data.Sqlite"
    };

    LinkedPort? GetOrResolvePort(string name, Dictionary<string, LinkedPort> ports, Text.SourceSpan span, string role)
    {
        if (ports.TryGetValue(name, out var p)) return p;

        if (_doc?.States is not null)
        {
            var matchedState = Enumerable.FirstOrDefault(_doc.States, s => s.Name.Equals(name, StringComparison.Ordinal));
            if (matchedState is not null)
            {
                var implicitPort = new LinkedPort(
                    new UsingDecl(PortSigil.Resource, name, PortKind.Neuron, name, null, span),
                    new ContractSchema(name, ContractKind.Neuron, new[] { "Value" }, false)
                );
                ports[name] = implicitPort;
                return implicitPort;
            }
        }

        var resolvedName = name;
        if (StandardAliases.TryGetValue(name, out var alias))
        {
            resolvedName = alias;
        }

        var schema = catalog.Resolve(resolvedName);
        if (schema is not null)
        {
            var implicitPort = new LinkedPort(
                new UsingDecl(PortSigil.Call, name,
                    schema.Kind switch {
                        ContractKind.Synapse => PortKind.Synapse,
                        _ => PortKind.Neuron
                    },
                    resolvedName, null, span),
                schema
            );
            ports[name] = implicitPort;
            return implicitPort;
        }

        diagnostics.Error("INO305",
            $"Undeclared {role} port '{name}'. Declare it with 'using'.", span);
        return null;
    }

    void CheckStmt(Stmt s, Dictionary<string, LinkedPort> ports, NeuronDoc doc)
    {
        switch (s)
        {
            case LetAskStmt l:
                GetOrResolvePort(l.Port, ports, l.Span, "neuron");
                CheckExpr(l.Prompt, ports, doc);
                break;
            case LetExprStmt le:
                CheckExpr(le.Value, ports, doc);
                break;
            case EmitStmt e:
                if (GetOrResolvePort(e.Port, ports, e.Span, "synapse") is { } lp)
                    foreach (var a in e.Args)
                    {
                        if (!lp.Schema.IsDeferred &&
                            !lp.Schema.Fields.Contains(a.Name, StringComparer.OrdinalIgnoreCase))
                            diagnostics.Error("INO302",
                                $"'{a.Name}' is not a field of synapse " +
                                $"'{lp.Schema.Fqn}'. Known: {string.Join(", ", lp.Schema.Fields)}.",
                                e.Span);
                        CheckExpr(a.Value, ports, doc);
                    }
                break;
            case SaveStmt sv:
                GetOrResolvePort(sv.Port, ports, sv.Span, "resource");
                CheckExpr(sv.Value, ports, doc);
                break;

            case RememberStmt r:
                CheckExpr(r.Text, ports, doc);
                if (r.Value is { } rv) CheckExpr(rv, ports, doc);
                break;
            case CountStmt c:
                if (!doc.Counters.Contains(c.Counter))
                    diagnostics.Error("INO304",
                        $"Counter '{c.Counter}' is used but not declared via " +
                        $"@telemetry:counter:{c.Counter}.", c.Span);
                break;
            case LogStmt lg:
                CheckExpr(lg.Message, ports, doc);
                break;
            case IfStmt ifs:
                CheckExpr(ifs.Cond, ports, doc);
                foreach (var st in ifs.ThenBody) CheckStmt(st, ports, doc);
                foreach (var st in ifs.ElseBody) CheckStmt(st, ports, doc);
                break;
            case ForEachStmt fes:
                CheckExpr(fes.SourceList, ports, doc);
                foreach (var st in fes.Body) CheckStmt(st, ports, doc);
                break;
            case FlowMappingStmt fm:
                CheckExpr(fm.Source, ports, doc);
                CheckExpr(fm.Target, ports, doc);
                if (fm.Source is PortRefExpr prSrc)
                {
                    GetOrResolvePort(prSrc.Name, ports, prSrc.Span, "neuron");
                }
                if (fm.Target is PortRefExpr prTgt)
                {
                    GetOrResolvePort(prTgt.Name, ports, prTgt.Span, "synapse");
                }
                break;
            case WriteStmt wr:
                CheckExpr(wr.Target, ports, doc);
                CheckExpr(wr.Value, ports, doc);
                if (wr.Target is PortRefExpr prWr)
                {
                    GetOrResolvePort(prWr.Name, ports, prWr.Span, "neuron");
                }
                else if (wr.Target is CallExpr callWr)
                {
                    GetOrResolvePort(callWr.Builtin, ports, callWr.Span, "neuron");
                }
                break;
        }
    }

    void CheckExpr(Expr e, Dictionary<string, LinkedPort> ports, NeuronDoc doc)
    {
        switch (e)
        {
            case FieldAccessExpr f:
                var fullFqn = $"{f.PortName}.{f.Field}";
                var fullSchema = catalog.Resolve(fullFqn);
                if (fullSchema is not null && !fullSchema.IsDeferred)
                {
                    GetOrResolvePort(fullFqn, ports, f.Span, "port");
                    break;
                }
                var lp = GetOrResolvePort(f.PortName, ports, f.Span, "port");
                if (lp is not null && lp.Decl.Kind == PortKind.Synapse && !lp.Schema.IsDeferred)
                {
                    if (!lp.Schema.Fields.Contains(f.Field, StringComparer.OrdinalIgnoreCase))
                        diagnostics.Error("INO301",
                            $"'{f.Field}' is not a field of '{lp.Schema.Fqn}'. " +
                            $"Known: {string.Join(", ", lp.Schema.Fields)}.", f.Span);
                }
                break;
            case CallExpr c:
                CheckExpr(c.Arg, ports, doc);
                break;
            case ArgsExpr a:
                foreach (var arg in a.Args) CheckExpr(arg.Value, ports, doc);
                break;
            case InterpExpr i:
                foreach (var part in i.Parts) CheckExpr(part, ports, doc);
                break;

        }
    }

    void CheckScenarioStep(ScenarioStep st, Dictionary<string, LinkedPort> ports, NeuronDoc doc)
    {
        switch (st)
        {
            case GivenNeuronReturns g:
                if (GetOrResolvePort(g.Port, ports, g.Span, "neuron") is null)
                    diagnostics.Error("INO306",
                        $"Scenario stubs undeclared neuron '{g.Port}'.", g.Span);
                CheckExpr(g.Value, ports, doc);
                break;
            case GivenPredicate gp:
                CheckExpr(gp.Subject, ports, doc);
                break;
            case WhenInject w:
                if (GetOrResolvePort(w.Port, ports, w.Span, "inbound") is null)
                    diagnostics.Error("INO307",
                        $"Scenario injects undeclared port '{w.Port}'.", w.Span);
                foreach (var a in w.Args) CheckExpr(a.Value, ports, doc);
                break;
            case ThenSynapseEmitted t:
                if (GetOrResolvePort(t.Port, ports, t.Span, "synapse") is null)
                    diagnostics.Error("INO308",
                        $"Scenario asserts undeclared synapse '{t.Port}'.", t.Span);
                if (t.WithValue is { } wv) CheckExpr(wv, ports, doc);
                break;
            case ThenResourceHas r:
                if (GetOrResolvePort(r.Port, ports, r.Span, "resource") is null)
                    diagnostics.Error("INO309",
                        $"Scenario asserts undeclared resource '{r.Port}'.", r.Span);
                CheckExpr(r.Value, ports, doc);
                break;
            case ThenCounter tc:
                if (!doc.Counters.Contains(tc.Counter))
                    diagnostics.Error("INO310",
                        $"Scenario asserts undeclared counter '{tc.Counter}'. " +
                        $"Declare it with @telemetry:counter:{tc.Counter}.", tc.Span);
                break;
        }
    }
}
