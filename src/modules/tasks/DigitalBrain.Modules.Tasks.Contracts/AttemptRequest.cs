using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.attempt-request")]
public sealed record AttemptRequest(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] NeuronId Worker,
    [property: Id(2)] AttemptId Attempt,
    [property: Id(3)] long Revision,
    [property: Id(4)] Goal Goal);
