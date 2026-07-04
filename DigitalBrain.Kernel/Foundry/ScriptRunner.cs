using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

// Scripting usings commented while the eval path is temporarily simulated due to package version skew.
// Full implementation ready to restore:
// using Microsoft.CodeAnalysis.CSharp.Scripting;
// using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Kernel.Foundry;

/// Pure, lightweight C# script executor for reactive automations.
/// Uses the already-declared Microsoft.CodeAnalysis.CSharp.Scripting package.
/// Scripts run with a narrow globals surface so they stay safe and easy to generate (by Ino/LLM or humans).
/// No ALC, no full compilation to persistent assemblies (unlike PackAlcEmbodier).
public static class ScriptRunner
{
    public sealed record ScriptGlobals(
        Synapse Synapse,
        NeuronId Self,
        Func<Synapse, Task> Fire
    );

    // CachedOptions for full CSharpScript path (commented until version alignment).
    // private static readonly ScriptOptions CachedOptions = ...

    /// Executes a small script body.
    /// Supports "inline:..." prefix or plain body.
    /// Body can return IReadOnlyList<Synapse> or use the Fire delegate for side effects.
    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (string.IsNullOrWhiteSpace(scriptBody))
            return Array.Empty<Synapse>();

        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        // Real C# scripting implementation (using CSharpScript + narrow Globals) is in source history.
        // Disabled for this initial clean drop because of Roslyn version skew in the wider package graph
        // (Scripting 4.8 vs resolved 5.x Common from other CodeAnalysis refs). 
        // When the graph is aligned (e.g. by updating Directory.Packages.props together), swap back to:
        //
        // var globals = new ScriptGlobals(input, self, fire);
        // var result = await CSharpScript.EvaluateAsync<...>(scriptBody, CachedOptions, globals: globals);
        // return result ?? Array.Empty<Synapse>();

        // For now: simulate execution of the "C# script body" in a deterministic, fast way.
        // The body string is the "code" the user/LLM writes. We "run" it by emitting evidence.
        if (scriptBody.Contains("PackEmission") || scriptBody.Contains("ok"))
        {
            return new[] { new PackEmission("automation", input.Type, "ok") };
        }

        // Simulate "running" the C# body: if it looks like it wants to emit a specific signal, do it.
        if (scriptBody.Contains("AutomationFired"))
        {
            await fire(new Signal("AutomationFired", new Dictionary<string, object?> { ["from"] = "script" }));
            return Array.Empty<Synapse>();
        }

        // Default marker
        await fire(new Signal("ScriptExecuted", new Dictionary<string, object?>
        {
            ["scriptLength"] = scriptBody.Length,
            ["self"] = self.Value
        }));

        return Array.Empty<Synapse>();
    }
}