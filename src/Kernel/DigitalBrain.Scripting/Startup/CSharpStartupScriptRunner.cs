using DigitalBrain.Abstractions;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DigitalBrain.Scripting.Startup;

internal sealed class CSharpStartupScriptRunner : IStartupScriptRunner
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(typeof(object).Assembly, typeof(IDigitalBrain).Assembly)
        .WithImports(
            "System",
            "System.Threading",
            "System.Threading.Tasks",
            "DigitalBrain.Abstractions");

    public async Task<StartupScriptRunResult> RunAsync(
        StartupScript script,
        IDigitalBrain brain,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await CSharpScript.RunAsync(
                script.Source,
                Options,
                new StartupScriptContext(brain, cancellationToken),
                typeof(StartupScriptContext),
                cancellationToken);

            return new StartupScriptRunResult(
                true,
                state.ReturnValue?.ToString() ?? "completed",
                []);
        }
        catch (CompilationErrorException exception)
        {
            return new StartupScriptRunResult(
                false,
                "Compilation failed.",
                exception.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new StartupScriptRunResult(false, exception.Message, []);
        }
    }
}
