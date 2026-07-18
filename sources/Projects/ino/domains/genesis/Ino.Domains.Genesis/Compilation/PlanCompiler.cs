using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Ino.Domains.Genesis.Compilation;

/// <summary>
/// Globals exposed to a dynamic neuron's script body. The body runs as
/// a top-level expression returning <see cref="NeuronResult"/>, with these
/// properties reachable as bare names — e.g.
/// <c>NeuronResult.Ok($"Got it: {Prompt}")</c>.
/// </summary>
public sealed class RoslynPlanGlobals
{
    public string Prompt { get; init; } = string.Empty;
    public string NeuronId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
}

/// <summary>
/// Wraps <c>Microsoft.CodeAnalysis.CSharp.Scripting</c> for two roles in
/// the L1 loop: compile-time validation (<see cref="ValidateAsync"/>) so
/// CreatorNeuron rejects malformed drafts before registering them, and
/// runtime execution (<see cref="ExecuteAsync"/>) so RoslynPlan can run
/// the registered body for every dispatched dynamic neuron.
///
/// Both paths share the same <see cref="ScriptOptions"/> — the script sees
/// <c>Ino.Core</c>, <c>Ino.Core.Hosting</c>, and <c>System.*</c> by default
/// and can refer to <see cref="RoslynPlanGlobals"/> properties as bare
/// names. <see cref="ValidateAsync"/> compiles without running so we
/// surface compile errors as <see cref="NeuronActivationFailed"/> rather
/// than crashing the next routing hop.
/// </summary>
public static class PlanCompiler
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithImports(
            "System",
            "System.Threading",
            "System.Threading.Tasks",
            "System.Linq",
            "System.Collections.Generic",
            "Ino.Core",
            "Ino.Core.Hosting")
        .WithReferences(
            typeof(NeuronResult).Assembly,
            typeof(INeuronPlan).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(System.Threading.Tasks.Task).Assembly);

    /// <summary>
    /// Compiles <paramref name="scriptBody"/> against the script host's
    /// references and globals. Returns null on success, or a human-readable
    /// diagnostic string on compilation failure.
    /// </summary>
    public static string? Validate(string scriptBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptBody);

        var script = CSharpScript.Create<NeuronResult>(scriptBody, Options, typeof(RoslynPlanGlobals));
        var diagnostics = script.Compile();
        var errors = diagnostics
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        return errors.Length == 0 ? null : string.Join("; ", errors);
    }

    /// <summary>
    /// Runs <paramref name="scriptBody"/> with the supplied
    /// <paramref name="globals"/> and returns the script's
    /// <see cref="NeuronResult"/>. Compilation errors propagate as
    /// <see cref="CompilationErrorException"/>; runtime exceptions
    /// propagate as the original exception type.
    /// </summary>
    public static async Task<NeuronResult> ExecuteAsync(
        string scriptBody,
        RoslynPlanGlobals globals,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptBody);
        ArgumentNullException.ThrowIfNull(globals);

        var state = await CSharpScript.RunAsync<NeuronResult>(scriptBody, Options, globals, typeof(RoslynPlanGlobals), ct);
        return state.ReturnValue ?? NeuronResult.Ok(string.Empty);
    }
}
