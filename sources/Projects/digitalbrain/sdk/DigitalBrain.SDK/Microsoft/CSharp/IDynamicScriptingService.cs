namespace DigitalBrain.SDK.Microsoft.CSharp;

public interface IDynamicScriptingService
{
    Task<ScriptResult> CompileAndExecuteAsync(string code, ExecutionContext context, CancellationToken ct);
}
