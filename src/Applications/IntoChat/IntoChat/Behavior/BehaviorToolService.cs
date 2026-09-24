using System.ComponentModel;
using System.Diagnostics;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace IntoChat;

internal sealed class BehaviorAuthoringOptions
{
    public bool AllowActivation { get; set; }
    public string? ModelProfile { get; set; }
}

internal sealed record BehaviorDraftInput(string? Source, string? Tests, string[]? ModuleIds, string? Name, string? Purpose, string[]? Triggers, string[]? Effects);
internal sealed record BehaviorDraftView(CodeDraftSnapshot Draft, BehaviorDescription? Description, CodeCheckSnapshot? Check);
internal sealed record BehaviorInspection(BehaviorSnapshot Program, CodeDraftSnapshot? Draft, BehaviorDescription? Description,
    CodeCheckSnapshot? Check, BehaviorLogPage Logs);

internal sealed class BehaviorToolService(IDigitalBrain brain, ContractCatalog catalog, IOptions<BehaviorAuthoringOptions> options, BehaviorCatalogStore? registry = null)
{
    public ScopedBehaviorTools ForScope(string scope) => new(brain, catalog, scope, options.Value.AllowActivation, registry);
}

[McpServerToolType]
internal sealed class ScopedBehaviorTools(IDigitalBrain brain, ContractCatalog catalog, string scope, bool allowActivation, BehaviorCatalogStore? registry = null)
{
    private ICodeDraft DraftOf(string id) => brain.Get<ICodeDraft>(BehaviorToolScope.Key(scope, id));
    private IBehaviorProgram ProgramOf(string id) => brain.Get<IBehaviorProgram>(BehaviorToolScope.Key(scope, id));
    private void RequireActivation() { if (!allowActivation) { throw new InvalidOperationException("Behavior activation is disabled by host policy."); } }

    [McpServerTool(Name = "behavior_contracts"), Description("Discover installed module IDs, neuron contracts and a single-source behavior template before authoring C#. Pass modules=[] first to discover installed modules, then select exact returned IDs (for example time and flutter). A behavior name is not a module ID.")]
    public ContractCatalogSnapshot Contracts(string[] modules) => catalog.Read(modules);

    [McpServerTool(Name = "behavior_draft"), Description("Read or write a behavior draft in one step. Omit request to read the current draft, description and latest validation. Supply request.source and request.tests to save them; the server reads the current revision itself, so never send a revision or operation id. Supply request.name to set the display metadata. Use meaningful xUnit tests that exercise the behavior.")]
    public async Task<BehaviorDraftView> EditDraft(string id, BehaviorDraftInput? request, CancellationToken ct)
    {
        var draftProxy = DraftOf(id);
        var draft = await draftProxy.Read(ct);
        if (request?.Source is { } source)
        {
            if (registry is not null) { await registry.Register(scope, id, ct); }
            draft = await draftProxy.Save(new(draft.Revision, Guid.NewGuid(), source, request.Tests ?? "", request.ModuleIds ?? []), ct);
        }
        BehaviorDescription? description = null;
        if (request?.Name is { } name)
        {
            var store = registry ?? throw new InvalidOperationException("Behavior catalog is unavailable.");
            var current = await store.Read(scope, id, ct);
            description = await store.Describe(scope, id, new(current.Revision, name, request.Purpose ?? "", request.Triggers ?? [], request.Effects ?? []), ct);
        }
        else if (registry is not null && request is null)
        {
            description = await registry.Read(scope, id, ct);
        }
        var check = draft.LatestCheckId is { } checkId ? await draftProxy.ReadCheck(checkId, ct) : null;
        return new(draft, description, check);
    }

    [McpServerTool(Name = "behavior_check"), Description("Compile and test the current draft revision in a contained process. The server reads the revision and mints the operation id. Set cancel=true to cancel the outstanding validation. Returns the terminal status, diagnostics and passing artifact reference.")]
    public async Task<CodeCheckSnapshot> Check(string id, bool? cancel, CancellationToken ct)
    {
        var draft = DraftOf(id);
        var snapshot = await draft.Read(ct);
        if (cancel == true)
        {
            if (snapshot.LatestCheckId is not { } outstanding) { throw new InvalidOperationException("There is no validation to cancel."); }
            await draft.CancelCheck(outstanding, ct);
            return await draft.ReadCheck(outstanding, ct);
        }
        BehaviorTestContract.RejectTestsThatNeverReferenceTheBehavior(snapshot.Source, snapshot.Tests);
        var check = await draft.Check(new(snapshot.Revision, Guid.NewGuid()), ct);
        var elapsed = Stopwatch.StartNew();
        while (check.Status is CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing && elapsed.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(250, ct);
            check = await draft.ReadCheck(check.OperationId, ct);
        }
        if (registry is not null && check.Status == CodeCheckStatus.Passed)
        { await registry.RememberChecked(scope, id, await draft.Read(ct), check, ct); }
        return check;
    }

    [McpServerTool(Name = "behavior_activate"), Description("Act on the current verified behavior. Use action=status to read authoritative deployment, readiness, execution and logs; deploy to activate the latest passing artifact; start, stop or rollback the deployment; delete to uninstall the behavior and stop its worker. The server reads the current revision and mints the operation id, so never send a revision or operation id.")]
    public async Task<BehaviorInspection> Activate(string id, string action, CancellationToken ct)
    {
        var normalized = (action ?? "").Trim().ToLowerInvariant();
        if (normalized is not ("status" or "deploy" or "start" or "stop" or "rollback" or "delete"))
        { throw new ArgumentException("Use action status, deploy, start, stop, rollback or delete.", nameof(action)); }
        if (normalized is "deploy" or "start" or "rollback") { RequireActivation(); }
        var program = ProgramOf(id);
        var current = await program.Read(ct);
        var snapshot = normalized switch
        {
            "deploy" => await DeployLatestCheck(program, current, id, ct),
            "start" => await program.Start(new(current.Revision, Guid.NewGuid()), ct),
            "stop" => await program.Stop(new(current.Revision, Guid.NewGuid()), ct),
            "rollback" => await program.Rollback(new(current.Revision, Guid.NewGuid(), PreviousDeployment(current)), ct),
            "delete" => await DeleteProgram(program, current, id, ct),
            _ => current,
        };
        var draftProxy = DraftOf(id);
        var draft = await draftProxy.Read(ct);
        var check = draft.LatestCheckId is { } checkId ? await draftProxy.ReadCheck(checkId, ct) : null;
        BehaviorDescription? description = null;
        if (registry is not null)
        {
            try { description = await registry.Read(scope, id, ct); }
            catch (KeyNotFoundException) { }
        }
        return new(snapshot, draft, description, check, await ReadRecentLogs(program, ct));
    }

    private async Task<BehaviorSnapshot> DeployLatestCheck(IBehaviorProgram program, BehaviorSnapshot current, string id, CancellationToken ct)
    {
        var draft = await DraftOf(id).Read(ct);
        if (draft.LatestCheckId is not { } checkId) { throw new InvalidOperationException("Validate the draft before deploying."); }
        var check = await DraftOf(id).ReadCheck(checkId, ct);
        if (check.Status != CodeCheckStatus.Passed || check.Artifact is not { } artifact || check.Revision != draft.Revision)
        { throw new InvalidOperationException("The latest validation did not pass for the current draft revision."); }
        return await program.Deploy(new(current.Revision, Guid.NewGuid(), artifact, "{}"), ct);
    }

    private async Task<BehaviorSnapshot> DeleteProgram(IBehaviorProgram program, BehaviorSnapshot current, string id, CancellationToken ct)
    {
        var result = await program.Delete(new(current.Revision, Guid.NewGuid()), ct);
        if (registry is not null) { await registry.Remove(scope, id, ct); }
        return result;
    }

    private static long PreviousDeployment(BehaviorSnapshot current)
    {
        var target = current.DesiredDeploymentRevision;
        var previous = current.Deployments
            .Where(d => target is null || d.Revision < target)
            .OrderByDescending(d => d.Revision)
            .FirstOrDefault();
        return previous?.Revision ?? throw new InvalidOperationException("No earlier deployment is retained to roll back to.");
    }

    internal static async Task<BehaviorLogPage> ReadRecentLogs(IBehaviorProgram program, CancellationToken ct)
    {
        var head = await program.ReadLogs(0, 1, ct);
        var after = Math.Max(0, head.LastSequence - 150);
        var page = await program.ReadLogs(after, 150, ct);
        return page with { Truncated = page.Truncated || after > 0 };
    }

    // Fine-grained operations retained for the workspace HTTP surface. They are not exposed as
    // model tools; the coarse tools above own the developer-mode authoring path.
    public Task<BehaviorDescription> Describe(string id, DescribeBehavior request, CancellationToken ct)
        => (registry ?? throw new InvalidOperationException("Behavior catalog is unavailable.")).Describe(scope, id, request, ct);
    public Task<BehaviorDescription> Description(string id, CancellationToken ct)
        => (registry ?? throw new InvalidOperationException("Behavior catalog is unavailable.")).Read(scope, id, ct);
    public Task<CodeDraftSnapshot> ReadDraft(string id, CancellationToken ct) => DraftOf(id).Read(ct);
    public async Task<CodeDraftSnapshot> SaveDraft(string id, SaveCodeDraft request, CancellationToken ct)
    {
        if (registry is not null) { await registry.Register(scope, id, ct); }
        return await DraftOf(id).Save(request, ct);
    }
    public async Task<CodeCheckSnapshot> CheckDraft(string id, CheckCodeDraft request, CancellationToken ct)
    {
        var draft = DraftOf(id);
        var snapshot = await draft.Read(ct);
        if (snapshot.Revision == request.Revision) { BehaviorTestContract.RejectTestsThatNeverReferenceTheBehavior(snapshot.Source, snapshot.Tests); }
        return await draft.Check(request, ct);
    }
    public async Task<CodeCheckSnapshot> ReadCheck(string id, Guid operationId, CancellationToken ct)
    {
        var draft = DraftOf(id);
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var check = await draft.ReadCheck(operationId, ct);
            if (check.Status is not (CodeCheckStatus.Queued or CodeCheckStatus.Building or CodeCheckStatus.Testing)
                || elapsed.Elapsed >= TimeSpan.FromSeconds(20))
            {
                if (registry is not null && check.Status == CodeCheckStatus.Passed)
                { await registry.RememberChecked(scope, id, await draft.Read(ct), check, ct); }
                return check;
            }
            await Task.Delay(250, ct);
        }
    }
    public Task CancelCheck(string id, Guid operationId, CancellationToken ct) => DraftOf(id).CancelCheck(operationId, ct);
    public Task<BehaviorSnapshot> ReadBehavior(string id, CancellationToken ct) => ProgramOf(id).Read(ct);
    public Task<BehaviorSnapshot> Deploy(string id, DeployBehavior request, CancellationToken ct) { RequireActivation(); return ProgramOf(id).Deploy(request, ct); }
    public Task<BehaviorSnapshot> Start(string id, ChangeBehaviorState request, CancellationToken ct) { RequireActivation(); return ProgramOf(id).Start(request, ct); }
    public Task<BehaviorSnapshot> Stop(string id, ChangeBehaviorState request, CancellationToken ct) => ProgramOf(id).Stop(request, ct);
    public Task<BehaviorSnapshot> Rollback(string id, RollbackBehavior request, CancellationToken ct) { RequireActivation(); return ProgramOf(id).Rollback(request, ct); }
    public Task<BehaviorLogPage> Logs(string id, long afterSequence, int limit, CancellationToken ct) => ProgramOf(id).ReadLogs(afterSequence, limit, ct);
    public async Task<BehaviorSnapshot> Delete(string id, CancellationToken ct)
    {
        var program = ProgramOf(id);
        return await DeleteProgram(program, await program.Read(ct), id, ct);
    }
}
