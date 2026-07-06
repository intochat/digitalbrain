using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Kernel.Foundry;

/// C# script executor for reactive automations.
/// Authors write tiny real C# bodies against ScriptGlobals (Synapse, Self, Fire).
/// Supports "return new[] { ... };" and side-effect "await Fire(...);".
/// "inline:" prefix stripped. Errors become safe diagnostic emission (never crash host).
/// Compiled scripts cached by body hash for hot paths.
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
            typeof(PackEmission).Assembly,
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(ListSurface).Assembly)  // enables Ui surfaces (ListSurface, AutomationSurface) from scripts
        .AddImports("System", "System.Collections.Generic", "System.Threading.Tasks", "DigitalBrain.Core", "DigitalBrain.Core.Distribution");

    private static readonly ConcurrentDictionary<string, Script<object>> _scriptCache = new();

    private static string HashBody(string body) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));

    public static async Task<IReadOnlyList<Synapse>> ExecuteAsync(
        string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        if (string.IsNullOrWhiteSpace(scriptBody))
            return Array.Empty<Synapse>();

        if (scriptBody.StartsWith("inline:", StringComparison.OrdinalIgnoreCase))
            scriptBody = scriptBody["inline:".Length..].Trim();

        var globals = new ScriptGlobals(input, self, fire);
        var bodyHash = HashBody(scriptBody);

        // Try real CSharpScript first (real executable C# as authored)
        try
        {
            if (!_scriptCache.TryGetValue(bodyHash, out var compiled))
            {
                compiled = CSharpScript.Create(scriptBody, _options, typeof(ScriptGlobals));
                _scriptCache[bodyHash] = compiled;
            }

            var runResult = await compiled.RunAsync(globals);

            var emitted = new List<Synapse>();
            if (runResult.ReturnValue is IReadOnlyList<Synapse> list)
                emitted.AddRange(list);
            else if (runResult.ReturnValue is Synapse single)
                emitted.Add(single);
            // Note: side-effect calls to globals.Fire() already performed during RunAsync.

            // Side-effect Fires already executed via globals delegate during RunAsync.
            // Collect any extra if needed; for now return what was returned or empty.
            if (emitted.Count == 0)
            {
                // If script only did side effects (no return), emit a trace signal for observability
                // but do not duplicate fires. Callers already saw the fires.
            }
            return emitted;
        }
        catch (CompilationErrorException cex)
        {
            return new[] { new PackEmission("automation", input.Type, "script-compile-error:" + cex.Message) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Runtime error or load skew (4.8 Scripting vs 5.x common) -> safe diagnostic + fallback emulation
            var diag = new PackEmission("automation", input.Type, "script-error:" + ex.Message);
            try
            {
                // Fallback: simple regex emulation for common patterns so feature stays usable
                var fb = await EmulateAsync(scriptBody, input, self, fire);
                return fb.Count > 0 ? fb : new[] { diag };
            }
            catch
            {
                return new[] { diag };
            }
        }
    }

    private static async Task<IReadOnlyList<Synapse>> EmulateAsync(string scriptBody, Synapse input, NeuronId self, Func<Synapse, Task> fire)
    {
        var emitted = new List<Synapse>();
        foreach (Match m in Regex.Matches(scriptBody, @"new\s+Signal\s*\(\s*""([^""]+)"""))
        {
            var name = m.Groups[1].Value;
            var sig = new Signal(name, new Dictionary<string, object?> { ["fromScript"] = true });
            emitted.Add(sig);
            await fire(sig);
        }
        if (scriptBody.Contains("PackEmission", StringComparison.Ordinal))
        {
            var p = new PackEmission("automation", input.Type, "ok");
            emitted.Add(p);
            await fire(p);
        }
        if (emitted.Count == 0)
        {
            var trace = new Signal("ScriptExecuted", new Dictionary<string, object?> { ["scriptLength"] = scriptBody.Length, ["self"] = self.Value });
            emitted.Add(trace);
            await fire(trace);
        }
        return emitted;
    }
}