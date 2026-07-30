using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.OS.McpHost;

[McpServerToolType]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the MCP server DI container via WithTools<DigitalBrainBehaviorTools>().")]
internal sealed class DigitalBrainBehaviorTools(IDigitalBrain brain, IGrainFactory grains)
{
    [McpServerTool(Name = McpHost.ReadBehaviorToolName)]
    [Description("Read the durable behavior revision snapshot for an owner-scoped behavior id.")]
    public Task<BehaviorSnapshot> ReadBehaviorAsync(
        [Description("Behavior id, for example 'com.digitalbrain.account-enrichment'")] string behaviorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        return brain.Get<IBehaviorNeuron>(behaviorId).Read();
    }

    [McpServerTool(Name = McpHost.ProposeBehaviorRevisionToolName)]
    [Description(
        "Propose a behavior revision with C# source and a .feature BDD spec. Compiles the proposal "
        + "without mutating the active revision and journals the artifact hash or compile failure.")]
    public Task<BehaviorSnapshot> ProposeBehaviorRevisionAsync(
        [Description("Behavior id, for example 'com.digitalbrain.account-enrichment'")] string behaviorId,
        [Description("Caller-generated command id")] string commandId,
        [Description("Single-file C# behavior program source")] string programSource,
        [Description("Gherkin feature text used as the install BDD gate")] string featureText,
        [Description("Feature file name without extension")] string featureName = "install",
        [Description("Human-readable display name")] string displayName = "",
        [Description("Human-readable description")] string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(programSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureText);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        return brain.Get<IBehaviorNeuron>(behaviorId).Propose(new ProposeBehaviorRevision(
            new CommandId(commandIdentity),
            programSource,
            new Dictionary<string, string>(StringComparer.Ordinal) { [featureName] = featureText },
            string.IsNullOrWhiteSpace(displayName) ? behaviorId : displayName,
            string.IsNullOrWhiteSpace(description) ? behaviorId : description));
    }

    [McpServerTool(Name = McpHost.RunBehaviorTestsToolName)]
    [Description("Run the BDD install gate against a proposed behavior artifact hash.")]
    public Task<BehaviorSnapshot> RunBehaviorTestsAsync(
        [Description("Behavior id")] string behaviorId,
        [Description("Caller-generated command id")] string commandId,
        [Description("Content-addressed artifact hash from propose")] string artifactHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        return brain.Get<IBehaviorNeuron>(behaviorId).RunTests(
            new RunBehaviorTests(new CommandId(commandIdentity), artifactHash));
    }

    [McpServerTool(Name = McpHost.ApproveBehaviorRevisionToolName)]
    [Description(
        "Approve a proposed behavior revision bound to an artifact hash. Requires a prior session "
        + "delivery of BehaviorRevisionApproval evidence and a green BDD gate.")]
    public async Task<BehaviorSnapshot> ApproveBehaviorRevisionAsync(
        [Description("Behavior id")] string behaviorId,
        [Description("Caller-generated command id")] string commandId,
        [Description("Artifact hash fingerprint to approve")] string artifactHash,
        [Description("Approval identity GUID")] string approvalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        if (!Guid.TryParse(approvalId, out var approvalIdentity) || approvalIdentity == Guid.Empty)
        {
            throw new ArgumentException("The approval id must be a non-empty GUID.", nameof(approvalId));
        }

        var approval = new BehaviorRevisionApproval(
            approvalIdentity,
            new CommandId(commandIdentity),
            artifactHash,
            ISessionNeuron.ForOwner(brain.Owner),
            DateTimeOffset.UtcNow);

        var neuron = brain.Get<IBehaviorNeuron>(behaviorId);
        await brain.SendAsync(NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId), approval);

        var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(brain.Owner).ToGrainId());
        var neuronId = NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId);
        var after = 0L;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var journal = await session.ReadNeuronJournal(neuronId, JournalKind.Incoming, after);
            if (journal.Delta.Any(delivery =>
                    delivery.Synapse is BehaviorRevisionApproval recorded
                    && recorded == approval
                    && delivery.Caller == approval.Approver))
            {
                break;
            }

            after = journal.ResumeSequence;
            await Task.Delay(20);
        }

        return await neuron.Approve(approval);
    }
}
