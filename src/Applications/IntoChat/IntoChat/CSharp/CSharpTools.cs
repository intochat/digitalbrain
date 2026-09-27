using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.CSharp;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace IntoChat;

internal sealed class CSharpAuthoringOptions
{
    public bool AllowActivation { get; set; }
}

internal sealed record CSharpFileView(CSharpDescription Description, CSharpFileSnapshot File, string? Logs = null);
internal sealed record WriteCSharpFileRequest(string Source, string? Name = null, string? Purpose = null);

// The contract catalog is registered by CSharpModule, so its absence means this host cannot run C# files.
internal sealed class CSharpToolService(IDigitalBrain brain, CSharpCatalogStore catalog, IOptions<CSharpAuthoringOptions> options,
    CSharpContractCatalog? contracts = null)
{
    public static readonly (MethodInfo Method, string Name)[] Tools = [.. typeof(ScopedCSharpTools).GetMethods()
        .Select(method => (Method: method, Tool: method.GetCustomAttribute<McpServerToolAttribute>()))
        .Where(candidate => candidate.Tool is not null)
        .Select(candidate => (candidate.Method, candidate.Tool!.Name!))];

    public bool CanRun => contracts is not null;

    public bool AllowActivation => CanRun && options.Value.AllowActivation;

    public ScopedCSharpTools ForScope(string scope) => new(brain, catalog, contracts, scope, AllowActivation);
}

[McpServerToolType]
internal sealed class ScopedCSharpTools(IDigitalBrain brain, CSharpCatalogStore catalog, CSharpContractCatalog? contracts, string scope, bool allowActivation)
{
    private const int LogTail = 150;

    public bool AllowActivation => allowActivation;

    [McpServerTool(Name = "csharp_contracts"), Description("Discover installed module IDs, neuron contracts, the #:project directive per module and an example single-file C# app. Pass modules=[] first, then select exact returned IDs (for example time and flutter).")]
    public CSharpContractCatalogSnapshot Contracts(string[] modules) => RequireModule().Read(modules);

    [McpServerTool(Name = "csharp_write"), Description("Save the full source of a single-file C# app and its readable name and purpose. The app connects with `await using var brain = await DigitalBrainClient.ConnectAsync(args);` and operates neurons through brain.Get<T>(id) and brain.On<TSignal>(neuron). Copy the #:project lines for the contracts you use from csharp_contracts. Saving does not restart a running app; use csharp_run start.")]
    public async Task<CSharpFileView> Write(string id, string source, string? name, string? purpose, CancellationToken ct)
    {
        // The catalog enforces name, purpose and per-workspace limits before any source is stored.
        var file = File(id);
        var description = await catalog.Describe(scope, id, name, purpose, ct);
        await file.Write(source, ct);
        return new(description, await file.Read(ct));
    }

    [McpServerTool(Name = "csharp_run"), Description("Act on a saved C# app: status reads its run state and recent output; start compiles and runs the current source in the C# sandbox (restarting it if running); stop stops it; logs reads recent console output; delete stops it and removes the file. Compile errors appear in logs and the status is Exited with a non-zero exit code. Never claim the app works until its logs show the expected output.")]
    public async Task<CSharpFileView> Run(string id, string action, CancellationToken ct) => (action ?? "").Trim().ToLowerInvariant() switch
    {
        "status" or "logs" => await Read(id, ct),
        "start" => await Start(id, ct),
        "stop" => await Stop(id, ct),
        "delete" => await Delete(id, ct),
        _ => throw new ArgumentException("Use action status, start, stop, logs or delete.", nameof(action)),
    };

    public async Task<IReadOnlyList<CSharpFileView>> List(CancellationToken ct)
    {
        var views = new List<CSharpFileView>();
        foreach (var description in await catalog.List(scope, ct))
        { views.Add(new(description, await File(description.Id).Read(ct))); }
        return views;
    }

    public async Task<CSharpFileView> Read(string id, CancellationToken ct)
    {
        var description = await catalog.Read(scope, id, ct);
        var file = File(id);
        return new(description, await file.Read(ct), await file.ReadLogs(LogTail, ct));
    }

    public async Task<CSharpFileView> Start(string id, CancellationToken ct)
    {
        if (!allowActivation) { throw new InvalidOperationException("Running C# files is disabled by host policy. Enable IntoChat:CSharp:AllowActivation."); }
        var description = await catalog.Read(scope, id, ct);
        return new(description, await File(id).Start(ct));
    }

    public async Task<CSharpFileView> Stop(string id, CancellationToken ct)
    {
        var description = await catalog.Read(scope, id, ct);
        return new(description, await File(id).Stop(ct));
    }

    public async Task<CSharpFileView> Delete(string id, CancellationToken ct)
    {
        var description = await catalog.Read(scope, id, ct);
        var file = File(id);
        await file.Delete(ct);
        await catalog.Remove(scope, id, ct);
        return new(description, await file.Read(ct));
    }

    private ICSharpFile File(string id)
    {
        RequireModule();
        return brain.Get<ICSharpFile>(CSharpCatalogStore.FileKey(scope, id));
    }

    private CSharpContractCatalog RequireModule()
        => contracts ?? throw new InvalidOperationException("This host does not run C# files; compose CSharpModule in the developer profile.");
}
