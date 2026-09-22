using System.ComponentModel;
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

internal sealed class BehaviorToolService(IDigitalBrain brain, ContractCatalog catalog, IOptions<BehaviorAuthoringOptions> options)
{
    public ScopedBehaviorTools ForScope(string scope) => new(brain, catalog, scope, options.Value.AllowActivation);
}

[McpServerToolType]
internal sealed class ScopedBehaviorTools(IDigitalBrain brain, ContractCatalog catalog, string scope, bool allowActivation)
{
    private ICodeDraft Draft(string id) => brain.Get<ICodeDraft>(BehaviorToolScope.Key(scope, id));
    private IBehaviorProgram Program(string id) => brain.Get<IBehaviorProgram>(BehaviorToolScope.Key(scope, id));
    private void RequireActivation() { if (!allowActivation) { throw new InvalidOperationException("Behavior activation is disabled by host policy."); } }

    [McpServerTool(Name = "code_contracts"), Description("Read installed neuron contracts and a single-source behavior template before authoring C#.")]
    public ContractCatalogSnapshot Contracts(string[] modules) => catalog.Read(modules);
    [McpServerTool(Name = "code_draft_read"), Description("Read current source, tests, and revision before editing a behavior draft.")]
    public Task<CodeDraftSnapshot> ReadDraft(string id, CancellationToken ct) => Draft(id).Read(ct);
    [McpServerTool(Name = "code_draft_save"), Description("Save source and tests using the expected revision. Does not execute or deploy.")]
    public Task<CodeDraftSnapshot> SaveDraft(string id, SaveCodeDraft request, CancellationToken ct) => Draft(id).Save(request, ct);
    [McpServerTool(Name = "code_draft_check"), Description("Compile and test this exact draft revision in a contained process. Returns a check operation to poll.")]
    public Task<CodeCheckSnapshot> CheckDraft(string id, CheckCodeDraft request, CancellationToken ct) => Draft(id).Check(request, ct);
    [McpServerTool(Name = "code_check_read"), Description("Read check status, diagnostics and the passing artifact reference.")]
    public Task<CodeCheckSnapshot> ReadCheck(string id, Guid operationId, CancellationToken ct) => Draft(id).ReadCheck(operationId, ct);
    [McpServerTool(Name = "code_check_cancel"), Description("Cancel a queued or active compile/test operation.")]
    public Task CancelCheck(string id, Guid operationId, CancellationToken ct) => Draft(id).CancelCheck(operationId, ct);
    [McpServerTool(Name = "behavior_read"), Description("Read authoritative deployment, readiness and execution state.")]
    public Task<BehaviorSnapshot> ReadBehavior(string id, CancellationToken ct) => Program(id).Read(ct);
    [McpServerTool(Name = "behavior_deploy"), Description("Activate a passing artifact when the user requested execution. Requires host execution policy and expected revision.")]
    public Task<BehaviorSnapshot> Deploy(string id, DeployBehavior request, CancellationToken ct) { RequireActivation(); return Program(id).Deploy(request, ct); }
    [McpServerTool(Name = "behavior_start"), Description("Start the current verified behavior deployment.")]
    public Task<BehaviorSnapshot> Start(string id, ChangeBehaviorState request, CancellationToken ct) { RequireActivation(); return Program(id).Start(request, ct); }
    [McpServerTool(Name = "behavior_stop"), Description("Persist stopped intent and terminate the current worker.")]
    public Task<BehaviorSnapshot> Stop(string id, ChangeBehaviorState request, CancellationToken ct) => Program(id).Stop(request, ct);
    [McpServerTool(Name = "behavior_rollback"), Description("Activate a retained verified artifact and its configuration as a new deployment revision.")]
    public Task<BehaviorSnapshot> Rollback(string id, RollbackBehavior request, CancellationToken ct) { RequireActivation(); return Program(id).Rollback(request, ct); }
    [McpServerTool(Name = "behavior_logs"), Description("Read bounded behavior logs with sequence and truncation information.")]
    public Task<BehaviorLogPage> Logs(string id, long afterSequence, int limit, CancellationToken ct) => Program(id).ReadLogs(afterSequence, limit, ct);
}
