using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Parsing;
using DigitalBrain.InoLang.Planning;

namespace DigitalBrain.InoLang;

public sealed class CompiledNeuron
{
    internal CompiledNeuron(ExecutionPlan? plan, LinkedNeuron? linked, IReadOnlyList<Diagnostic> diags)
    {
        Plan = plan;
        Linked = linked;
        Diagnostics = diags;
    }

    public ExecutionPlan? Plan { get; }
    // E-RUN #36: producer-side consumers (LinkedPortCatalogContributor) need the
    // linker product to derive the descriptor's Incoming[]/Outgoing pairs from
    // the same source of truth the Plan was lowered from.
    public LinkedNeuron? Linked { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool Success => Plan is not null &&
        !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public Task<GateDecision> EvaluateGateAsync(CancellationToken ct)
        => Success
            ? InoGate.EvaluateAsync(Plan!, ct)
            : Task.FromResult(new GateDecision(false,
                "compilation failed — cannot gate."));
}

// The single entry point. Plan 2 (E-RUN) calls this from the InterpretedNeuronGrain.
public static class InoCompiler
{
    // v5 C1: the no-catalog overload defers all FQN resolution to activation.
    // Used by the boot floor and by Creator's LLM-authored .ino flow, neither
    // of which has a static catalog to validate against. Field-level shape
    // checks are skipped (DeferredContractCatalog returns IsDeferred schemas;
    // the Linker honors that flag).
    public static CompiledNeuron Compile(string source)
        => Compile(source, DeferredContractCatalog.Instance);

    public static CompiledNeuron Compile(string source, IContractCatalog catalog)
    {
        var bag = new DiagnosticBag();
        var tokens = new Lexer(source, bag).Lex();
        var doc = new Parser(tokens, bag).ParseDocument();
        if (doc is null || bag.HasErrors)
            return new CompiledNeuron(null, null, bag.Items);

        var linked = new Linker(catalog, bag).Link(doc);
        if (linked is null || bag.HasErrors)
            return new CompiledNeuron(null, null, bag.Items);

        return new CompiledNeuron(Lowering.Lower(linked), linked, bag.Items);
    }
}
