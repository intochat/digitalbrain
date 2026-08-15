using Brain.Abstractions.Runtime;
using Orleans.Concurrency;

namespace Brain.Modules.AI;

public interface IAssistantNeuron : IGrainWithStringKey
{
    Task<string> ChatAsync(AssistantNeuronRequest request);
}

[GenerateSerializer, Immutable]
public sealed record AssistantNeuronRequest(
    [property: Id(0)] Guid ActivityId,
    [property: Id(1)] BrainOperationInvocation Invocation,
    [property: Id(2)] string Message);
