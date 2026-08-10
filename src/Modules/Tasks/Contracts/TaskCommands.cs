using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.policy")]
public sealed record TaskPolicy(
    [property: Id(0)] int MaximumAttempts,
    [property: Id(1)] TimeSpan RetryDelay,
    [property: Id(2)] DateTimeOffset? Deadline);

[GenerateSerializer]
[Alias("tasks.start")]
[Description("Start a durable owner-scoped task")]
public sealed record StartTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] Goal Goal,
    [property: Id(2)] NeuronId Worker,
    [property: Id(3)] TaskPolicy Policy,
    [property: Id(4)] NeuronId? RetryOf = null) : RequestSynapse<TaskSnapshot>;

[GenerateSerializer]
[Alias("tasks.cancel")]
public sealed record CancelTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision);
