namespace DigitalBrain.Apps;

public interface IScriptSandbox
{
    bool CanRun { get; }
    string AuthoringDescription { get; }
    Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken);
    ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files);
}

public sealed record ScriptContractModule(string Id, string? Directive);
public sealed record ScriptContractCatalog(IReadOnlyList<ScriptContractModule> Modules, IReadOnlyList<string> Contracts, string Example, bool Truncated = false);
public sealed record ScriptCompilationDiagnostic(string File, string Id, int Line, string Message);
public sealed record ScriptCompilationCheck(bool Success, IReadOnlyList<ScriptCompilationDiagnostic> Errors);
