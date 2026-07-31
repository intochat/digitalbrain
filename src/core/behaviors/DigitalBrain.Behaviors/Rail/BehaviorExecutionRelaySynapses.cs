using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal static class BehaviorExecutionRelay
{
    internal const string GrainTypeName = "behaviors.execution-relay";
}

[GenerateSerializer]
[Alias("behaviors.relay-hosted-execution")]
[Description("One-shot Worker→relay envelope; relay re-stages run work so Worker outbox drain is not held")]
internal sealed record RelayHostedBehaviorExecution(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptRequest Attempt,
    [property: Id(2)] BehaviorExecutionId Execution,
    [property: Id(3)] DateTimeOffset UtcNow) : Synapse;

[GenerateSerializer]
[Alias("behaviors.run-hosted-execution")]
[Description("Relay self-message that performs hosted execution after the Worker drain turn has ended")]
internal sealed record RunHostedBehaviorExecution(
    [property: Id(0)] NeuronId Worker,
    [property: Id(1)] AttemptRequest Attempt,
    [property: Id(2)] BehaviorExecutionId Execution,
    [property: Id(3)] DateTimeOffset UtcNow) : Synapse;

[GenerateSerializer]
[Alias("behaviors.complete-hosted-execution")]
[Description("Relay→Worker terminal completion for a staged hosted execution")]
internal sealed record CompleteHostedBehaviorExecution(
    [property: Id(0)] AttemptRequest Attempt,
    [property: Id(1)] bool Succeeded,
    [property: Id(2)] string StableCode,
    [property: Id(3)] bool Cancelled) : Synapse;
