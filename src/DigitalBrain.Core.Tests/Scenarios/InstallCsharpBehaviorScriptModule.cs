using System.Collections.Immutable;
using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record BehaviorInstallProposed(
    string BehaviorId,
    string ScriptKind,
    ImmutableArray<string> Listens) : Synapse;

public sealed record BehaviorActivated(
    string BehaviorId,
    string ScriptKind,
    ImmutableArray<string> Listens) : Synapse;

// Stage-1 stand-in for behavior catalog: install is journaled as facts, not ALC hot-load.
public sealed class BehaviorCatalog : Neuron, INeuron<BehaviorInstallProposed>
{
    public Task HandleAsync(BehaviorInstallProposed fact, CancellationToken cancellationToken)
    {
        Emit(new BehaviorActivated(fact.BehaviorId, fact.ScriptKind, fact.Listens));
        return Task.CompletedTask;
    }
}

// Catalog sink so BehaviorActivated ambient Emit is legal.
public sealed class BehaviorActivationLedger : Neuron, INeuron<BehaviorActivated>
{
    public Task HandleAsync(BehaviorActivated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Script neuron kind present in the composition catalog (honest Stage-1: not dynamic load).
// Hears EmailReceived and materializes a task when the sender domain is VIP.
public sealed class VipEmailToTask : Neuron, INeuron<EmailReceived>
{
    public const string BehaviorId = "vip-email-to-task";
    public const string VipDomain = "board.example";

    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.Domain, VipDomain, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        Emit(new TaskCreated(
            TaskId: $"task-{fact.MessageId}",
            Title: fact.Subject,
            SourceMessageId: fact.MessageId,
            Tag: "vip"));
        Emit(new BehaviorNudge(BehaviorId, fact.MessageId, ChipLabel: "VIP"));
        return Task.CompletedTask;
    }
}
