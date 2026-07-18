using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.SDK.Microsoft.CSharp;

public sealed class DynamicScriptingService : IDynamicScriptingService
{
    public async Task<ScriptResult> CompileAndExecuteAsync(string code, ExecutionContext context, CancellationToken ct)
    {
        try
        {
            var options = ScriptOptions.Default
                .WithReferences(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)))
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.Text.Json",
                    "System.Threading.Tasks",
                    "Microsoft.Extensions.DependencyInjection");

            var globals = new DynamicScriptingGlobals(context.Globals, context.Services);

            var script = CSharpScript.Create<object>(code, options, globalsType: typeof(DynamicScriptingGlobals));
            
            var compileDiagnostics = script.Compile(ct);
            var errors = compileDiagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
                .ToList();

            if (errors.Count > 0)
            {
                return new ScriptResult(false, null, errors);
            }

            var state = await script.RunAsync(globals, cancellationToken: ct);
            return new ScriptResult(true, state.ReturnValue, errors);
        }
        catch (CompilationErrorException cex)
        {
            return new ScriptResult(false, null, cex.Diagnostics.Select(d => d.ToString()).ToList());
        }
        catch (Exception ex)
        {
            return new ScriptResult(false, null, new[] { ex.Message }, ex);
        }
    }
}

public sealed class DynamicScriptingGlobals(IReadOnlyDictionary<string, object> globals, IServiceProvider services)
{
    public IReadOnlyDictionary<string, object> Globals { get; } = globals;
    public IServiceProvider Services { get; } = services;
}
