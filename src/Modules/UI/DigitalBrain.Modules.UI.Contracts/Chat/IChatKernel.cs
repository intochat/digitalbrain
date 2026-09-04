using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;
using DigitalBrain.Product.Interactions;

namespace DigitalBrain.Chat;

// In-silo return-value surface. Owners and scripts use IChat + SendAsync/RequestAsync.
// GrainFactory.GetGrain<IChatKernel> is how ChatTurnWorker and context providers read
// without nesting another BrainNeuron.Send (that deadlocks the session neuron).
[Alias("chat.runtime")]
public interface IChatKernel : IGrainWithStringKey
{
    Task<ChatTranscript> LoadTranscript();

    Task<IReadOnlyList<ChatTurnSnapshot>> LoadTurnSnapshots();

    Task<ExecutionId?> LoadActiveExecution();

    Task SaveActiveExecution(ExecutionId? id);

    Task CompleteUserAction(AgentTurnContext context, string actionId, bool accepted);
}
