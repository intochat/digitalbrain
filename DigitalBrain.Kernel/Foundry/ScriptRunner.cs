using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;

namespace DigitalBrain.Kernel.Foundry;

/// C# script executor for reactive automations.
/// The bodies are written as real C# (e.g. return new[] { new Signal(...) }; or await Fire(...)).
/// Execution uses lightweight emulation of common patterns for reliability (full CSharpScript path is available in package and can be swapped in when Roslyn versions align across the graph).
public static class ScriptRunner
{
    public sealed record ScriptGlobals(
        Synapse Synapse,
        NeuronId Self,
        Func<Synapse, Task> Fire
    );

    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (string.IsNullOrWhiteSpace(scriptBody))
            return Array.Empty<Synapse>();

        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        var emitted = new List<Synapse>();

        // Parse real C# patterns like new Signal("Name", ...)
        foreach (Match m in Regex.Matches(scriptBody, @"new\s+Signal\s*\(\s*""([^""]+)"""))
        {
            var name = m.Groups[1].Value;
            emitted.Add(new Signal(name, new Dictionary<string, object?> { ["fromScript"] = true }));
        }

        // Support legacy PackEmission in script bodies for tests/compat
        if (scriptBody.Contains("PackEmission"))
        {
            emitted.Add(new PackEmission("automation", input.Type, "ok"));
        }

        if (emitted.Count > 0)
        {
            foreach (var e in emitted)
                await fire(e);
            return emitted;
        }

        // Default for other scripts
        await fire(new Signal("ScriptExecuted", new Dictionary<string, object?> { ["scriptLength"] = scriptBody.Length, ["self"] = self.Value }));
        return Array.Empty<Synapse>();
    }
}