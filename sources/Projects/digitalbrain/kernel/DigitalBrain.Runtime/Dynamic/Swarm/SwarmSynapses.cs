using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic.Swarm;

[GenerateSerializer]
public sealed record SwarmTrigger([property: Id(1)] Guid SessionId,
    [property: Id(2)] string Payload,
    [property: Id(3)] int WorkerCount
) : Synapse;

[GenerateSerializer]
public sealed record SwarmTaskDispatched([property: Id(1)] Guid SessionId,
    [property: Id(2)] int WorkerIndex,
    [property: Id(3)] string TaskDescription
) : Synapse;

[GenerateSerializer]
public sealed record SwarmTaskCompleted([property: Id(1)] Guid SessionId,
    [property: Id(2)] int WorkerIndex,
    [property: Id(3)] string Result
) : Synapse;
