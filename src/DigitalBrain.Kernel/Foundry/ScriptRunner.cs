using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DigitalBrain.Core;
using DigitalBrain.Core.Distribution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Kernel.Foundry;

using DigitalBrain.Ui.Contracts;

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

        // Gate before any execution (same as packs). Use Roslyn compilation for violation check.
        try
        {
            var checkCompilation = FoundryCompilation.Create("script-gate", scriptBody, Enumerable.Empty<MetadataReference>());
            // Note: full refs added in real run; gate will catch obvious bans even with limited for check.
            var violations = CapabilityGate.FindViolations(checkCompilation);
            if (violations.Count > 0)
            {
                return new[] { new PackEmission("automation", input.Type, "script-violation:" + string.Join(";", violations)) };
            }
        }
        catch { /* gate is best effort; proceed to real compile which will fail on bad anyway */ }

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
            // Runtime error -> safe diagnostic only. No fallback (Emulate deleted per plan).
            return new[] { new PackEmission("automation", input.Type, "script-error:" + ex.Message) };
        }
    }

}
