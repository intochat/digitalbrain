namespace DigitalBrain.Assistant;

public sealed record AgentReceipt(
    AgentRunOutcome Outcome,
    string Summary,
    IReadOnlyList<AgentCall> Calls,
    IReadOnlyList<AgentTouchedData> Touched,
    int ModelCalls,
    decimal Compute);
