using DigitalBrain.Apps;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpScriptSandbox(CSharpToolService tools) : IScriptSandbox
{
    public bool CanRun => tools.CanRun;
    public string AuthoringDescription => """
        - "csharp": one C# file-based app per concern under "files" as "behaviors/<name>.cs" (a single
                  behavior is fine). Each behavior answers invocations or reacts to signals through the brain
                  client. The invocation-answering shape (change only the Answer function, keep the #:project line):
                    #:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
                    using DigitalBrain.Apps;
                    using DigitalBrain.Apps.Signals;

                    await using var brain = await DigitalBrainClient.ConnectAsync(args);
                    var app = brain.Get<IApp>(brain.Setting("App")!);
                    await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
                    foreach (var missed in await app.Pending()) { await app.Respond(Answer(missed.Id, missed.Input)); }
                    await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(Answer(invoked.InvocationId, invoked.Input)); }

                    static AppResponse Answer(Guid invocationId, string input) => new(invocationId, /* the answer */ input, null);
                  Settings are read with brain.Setting("Name").
        """;


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
