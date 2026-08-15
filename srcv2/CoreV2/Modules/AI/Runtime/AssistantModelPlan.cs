namespace Brain.Modules.AI;

public sealed record AssistantToolCall(string OperationId, string InputJson);

public sealed record AssistantModelPlan(
    IReadOnlyList<AssistantToolCall> Calls,
    string Response);
