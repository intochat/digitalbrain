using Brain.Abstractions.Runtime;

namespace Brain.Modules.AI;

public interface IAssistantChatModel
{
    Task<AssistantModelPlan> PlanAsync(
        string message,
        IReadOnlyList<BrainOperationDescriptor> operations,
        CancellationToken cancellationToken);
}
