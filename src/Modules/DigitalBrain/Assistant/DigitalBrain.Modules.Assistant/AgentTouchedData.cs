namespace DigitalBrain.Assistant;

public sealed record AgentTouchedData(string Source, string SemanticTypeId, bool ReadOnly, long RowsRead);
