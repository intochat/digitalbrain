namespace DigitalBrain.Assistant;

public sealed record AgentCall(string AppId, string Operation, bool Discovered, bool Succeeded);
