using DigitalBrain.Apps;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpScriptSandbox(CSharpToolService tools) : IScriptSandbox
{
    public bool CanRun => tools.CanRun;

    public async Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
    {
        var discovery = tools.Discovery ?? throw new InvalidOperationException("This host has no script contract discovery.");
        var catalog = await discovery.Read(modules, cancellationToken);
        return new([.. catalog.Modules.Select(module => new ScriptContractModule(module.Id, module.Directive))],
            catalog.Contracts, catalog.Example, catalog.Truncated);
    }

    public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files)
    {
        var checker = tools.Check ?? throw new InvalidOperationException("This host has no script compiler.");
        var result = checker.Check(files);
        return new(result.Success, [.. result.Errors.Select(error => new ScriptCompilationDiagnostic(error.File, error.Id, error.Line, error.Message))]);
    }
}
