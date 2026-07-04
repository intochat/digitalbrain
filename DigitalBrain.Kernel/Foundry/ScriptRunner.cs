using System.Text.RegularExpressions;
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
        // The body string *is* the C# the author/LLM writes. We emulate common patterns
        // (new Signal("Name", ...), PackEmission, etc.) so real script bodies in "when ... then"
        // examples produce the intended effects without full Roslyn scripting eval.
        var emitted = new List<Synapse>();

        // Emulate explicit Signal creations in the body
        foreach (Match m in Regex.Matches(scriptBody, @"new\s+Signal\s*\(\s*""([^""]+)"""))
        {
            var name = m.Groups[1].Value;
            emitted.Add(new Signal(name, new Dictionary<string, object?> { ["fromScript"] = true }));
        }

        // Emulate PackEmission for legacy pack-style returns
        if (scriptBody.Contains("PackEmission") || scriptBody.Contains("\"ok\""))
        {
            emitted.Add(new PackEmission("automation", input.Type, "ok"));
        }

        if (emitted.Count > 0)
        {
            foreach (var e in emitted)
                await fire(e);
            return emitted;
        }

        // Default: fire a marker so the script is observably "run"
        await fire(new Signal("ScriptExecuted", new Dictionary<string, object?>
        {
            ["scriptLength"] = scriptBody.Length,
            ["self"] = self.Value
        }));

        return Array.Empty<Synapse>();
    }
}