namespace DigitalBrain.Assistant;

public sealed record AgentCall(string AppId, string Operation, bool Discovered, bool Succeeded,
    string? CallId = null, string? ErrorCode = null, string? ErrorMessage = null);
