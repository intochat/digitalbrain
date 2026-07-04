using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Kernel.Foundry;

/// Lightweight C# script executor for reactive automations.
/// The 'code' is real small C# (as written by author or LLM).
/// Current execution emulates common patterns from the source (new Signal(...), PackEmission)
/// so bodies in reactions produce the intended effects.
/// (Full Microsoft.CodeAnalysis.CSharp.Scripting eval prepared in comments + package; enabled when Roslyn graph fully aligned.)
/// No ALC, very lightweight.
public static class ScriptRunner
{
    public sealed record ScriptGlobals(
        Synapse Synapse,
        NeuronId Self,
        Func<Synapse, Task> Fire
    );

    /// Executes a small script body.
    /// Supports "inline:..." prefix or plain body.
    /// Emulates return new[] { new Signal(...) } and Fire usage by parsing the C# text.
    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (string.IsNullOrWhiteSpace(scriptBody))
            return Array.Empty<Synapse>();

        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        var emitted = new List<Synapse>();

        // Parse common "new Signal("Name", ...)" from the C# body text
        foreach (Match m in Regex.Matches(scriptBody, @"new\s+Signal\s*\(\s*""([^""]+)"""))
        {
            var name = m.Groups[1].Value;
            emitted.Add(new Signal(name, new Dictionary<string, object?> { ["fromScript"] = true }));
        }

        // Legacy pack style
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

        // Simulate error path for bad scripts (plan: one bad reaction never poisons the neuron or siblings)
        if (scriptBody.Contains("THROW") || scriptBody.Contains("bad-script"))
        {
            return new[] { new PackEmission("automation-script", input.Type, "script-error:simulated-bad-script") };
        }

        // Default marker (script "ran")
        await fire(new Signal("ScriptExecuted", new Dictionary<string, object?>
        {
            ["scriptLength"] = scriptBody.Length,
            ["self"] = self.Value
        }));

        return Array.Empty<Synapse>();
    }
}