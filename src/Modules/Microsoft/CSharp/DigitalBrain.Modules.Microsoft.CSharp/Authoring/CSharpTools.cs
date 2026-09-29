using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.CSharp;
using ModelContextProtocol.Server;

namespace DigitalBrain.Microsoft.CSharp;

public sealed record CSharpFileView(CSharpDescription Description, CSharpFileSnapshot File, string? Logs = null);
public sealed record WriteCSharpFileRequest(string Source, string? Name = null, string? Purpose = null);

// The contract catalog is registered by CSharpModule, so its absence means this host cannot run C# files.
public sealed class CSharpToolService(IDigitalBrain brain, CSharpCatalogStore catalog, CSharpContractCatalog? contracts = null)
{
    public static readonly (MethodInfo Method, string Name)[] Tools = [.. typeof(ScopedCSharpTools).GetMethods()
        .Select(method => (Method: method, Tool: method.GetCustomAttribute<McpServerToolAttribute>()))
        .Where(candidate => candidate.Tool is not null)
        .Select(candidate => (candidate.Method, candidate.Tool!.Name!))];

    public bool CanRun => contracts is not null;

    public ScopedCSharpTools ForScope(string scope) => new(brain, catalog, contracts, scope, CanRun);
}

[McpServerToolType]
public sealed class ScopedCSharpTools(IDigitalBrain brain, CSharpCatalogStore catalog, CSharpContractCatalog? contracts, string scope, bool canRun)
{
    private const int LogTail = 150;

    public bool CanRun => canRun;

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

    [McpServerTool(Name = "csharp_run"), Description("Act on a saved C# app: status reads its run state and recent output; start compiles and runs the current source in the C# sandbox (restarting it if running) and keeps it running: a crash is retried up to 5 times and a lost sandbox is restarted; stop stops it; logs reads recent console output; delete stops it and removes the file. Compile errors appear in logs with a non-zero exit code while the status is Restarting, then Exited. Never claim the app works until its logs show the expected output.")]
    public async Task<CSharpFileView> Run(string id, string action, CancellationToken ct) => (action ?? "").Trim().ToLowerInvariant() switch
    {
        "status" or "logs" => await Read(id, ct),
        "start" => await Start(id, ct),
        "stop" => await Stop(id, ct),
        "delete" => await Delete(id, ct),
        _ => throw new ArgumentException("Use action status, start, stop, logs or delete.", nameof(action)),
    };

    [McpServerTool(Name = "csharp_arm"), Description("Run a saved C# app on a trigger instead of keeping it up: every signal of the given type from the given neuron starts one run, and the app reads that signal with brain.Trigger<TSignal>() and exits. neuron is the neuron id \"<grain type>/<key>\" from csharp_contracts, for example timer/tea; signal is the signal type name, for example TimerTick. Nothing runs between signals. stop disarms it; after 5 failing runs in a row it disarms itself.")]
    public async Task<CSharpFileView> Arm(string id, string neuron, string signal, CancellationToken ct)
    {
        if (!canRun) { throw new InvalidOperationException("This host has no C# sandbox, so it cannot run C# files."); }
        var description = await catalog.Read(scope, id, ct);
        return new(description, await File(id).Arm(new(neuron, signal), ct));
    }

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
        if (!canRun) { throw new InvalidOperationException("This host has no C# sandbox, so it cannot run C# files."); }
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
        => contracts ?? throw new InvalidOperationException("This host does not run C# files; compose CSharpModule.");
}
