using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Kernel.Foundry;

/// Real C# script executor for reactive automations (priority 1).
/// Accepts small C# bodies exactly as written by authors/LLM/Ino.
/// Supports both `return new[] { ... }` and `await Fire(...)` via narrow globals.
/// Uses Microsoft.CodeAnalysis.CSharp.Scripting (pinned to 4.8.0 for compatibility).
/// No collectible ALC (lightweight vs full packs).
public static class ScriptRunner
{
    public sealed record ScriptGlobals(
        Synapse Synapse,
        NeuronId Self,
        Func<Synapse, Task> Fire
    );

    private static readonly ScriptOptions _options = ScriptOptions.Default
        .AddReferences(
            typeof(Synapse).Assembly,
            typeof(Signal).Assembly,
            typeof(NeuronId).Assembly,
            typeof(PackEmission).Assembly)
        .AddImports("System", "System.Collections.Generic", "DigitalBrain.Core", "DigitalBrain.Core.Distribution");

    /// Executes a small script body.
    /// "inline:..." prefix supported for convenience.
    /// Real eval with CSharpScript. Errors become diagnostic PackEmission (never poison host).
    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (string.IsNullOrWhiteSpace(scriptBody))
            return Array.Empty<Synapse>();

        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        var globals = new ScriptGlobals(input, self, fire);

        try
        {
            // Use RunAsync + ReturnValue to support statement blocks with 'return' and side-effect 'await Fire(...)'
            var script = CSharpScript.Create<IReadOnlyList<Synapse>>(scriptBody, globalsType: typeof(ScriptGlobals), options: _options);
            var result = await script.RunAsync(globals);
            return result.ReturnValue ?? Array.Empty<Synapse>();
        }
        catch (Exception ex)
        {
            var msg = ex.GetBaseException().Message;
            return new[] { new PackEmission("automation-script", input.Type, "script-error:" + msg) };
        }
    }
}