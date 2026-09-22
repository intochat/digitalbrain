using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Options;

namespace IntoChat;

internal sealed record BehaviorListItem(BehaviorDescription Description, BehaviorSnapshot Program, long DraftRevision, CodeCheckSnapshot? Check);
internal sealed record BehaviorDetail(BehaviorDescription Description, BehaviorSnapshot Program, CodeDraftSnapshot Draft,
    CodeCheckSnapshot? Check, BehaviorLogPage Logs, bool AllowActivation, bool CanDeploy, CodeDraftSnapshot? DeployedSource, bool AllowValidation);

internal sealed class BehaviorManagement(IDigitalBrain brain, BehaviorCatalogStore catalog, IOptions<BehaviorAuthoringOptions> options,
    IOptions<CodeExecutionOptions> coding, IOptions<BehaviorOptions> runtime)
{
    private bool HasCoding => !string.IsNullOrWhiteSpace(coding.Value.Root);
    private bool HasRuntime => !string.IsNullOrWhiteSpace(runtime.Value.Root);
    private bool AllowActivation => HasCoding && HasRuntime && options.Value.AllowActivation;
    private Task<CodeDraftSnapshot> ReadDraft(string key, CancellationToken ct) => HasCoding ? brain.Get<ICodeDraft>(key).Read(ct) : Task.FromResult(new CodeDraftSnapshot(0, "", "", [], null));
    private Task<BehaviorSnapshot> ReadProgram(string key, CancellationToken ct) => HasRuntime ? brain.Get<IBehaviorProgram>(key).Read(ct)
        : Task.FromResult(new BehaviorSnapshot(0, BehaviorDesiredState.Stopped, BehaviorExecutionState.Stopped, null, null, null, false, null, []));
    public async Task<object> List(string scope, CancellationToken ct)
    {
        var items = new List<BehaviorListItem>();
        foreach (var description in await catalog.List(scope, ct))
        {
            var key = BehaviorToolScope.Key(scope, description.Id);
            var draft = await ReadDraft(key, ct);
            var check = draft.LatestCheckId is { } checkId ? await brain.Get<ICodeDraft>(key).ReadCheck(checkId, ct) : null;
            items.Add(new(description, await ReadProgram(key, ct), draft.Revision, check));
        }
        return new { items, allowActivation = AllowActivation };
    }

    public async Task<BehaviorDetail> Detail(string scope, string id, CancellationToken ct)
    {
        var description = await catalog.Read(scope, id, ct);
        var key = BehaviorToolScope.Key(scope, id);
        var draft = await ReadDraft(key, ct);
        var check = draft.LatestCheckId is { } checkId ? await brain.Get<ICodeDraft>(key).ReadCheck(checkId, ct) : null;
        var program = await ReadProgram(key, ct);
        await catalog.RememberChecked(scope, id, draft, check, ct);
        var active = program.Deployments.SingleOrDefault(x => x.Revision == program.ActiveDeploymentRevision);
        var source = active is null ? null : await catalog.CheckedSource(scope, id, active.Artifact.Id, ct);
        var logs = HasRuntime ? await ReadRecentLogs(brain.Get<IBehaviorProgram>(key), ct) : new BehaviorLogPage([], 0, false);
        return new(description, program, draft, check, logs, AllowActivation, CanDeploy(draft, check) && AllowActivation, source, HasCoding);
    }

    internal static bool CanDeploy(CodeDraftSnapshot draft, CodeCheckSnapshot? check)
        => check is { Status: CodeCheckStatus.Passed, Artifact: not null } && check.Revision == draft.Revision && check.OperationId == draft.LatestCheckId;

    internal static async Task<BehaviorLogPage> ReadRecentLogs(IBehaviorProgram program, CancellationToken ct)
    {
        // LastSequence is the log's global high-water mark, not the page end.
        var head = await program.ReadLogs(0, 1, ct);
        var after = Math.Max(0, head.LastSequence - 150);
        var page = await program.ReadLogs(after, 150, ct);
        return page with { Truncated = page.Truncated || after > 0 };
    }
}
