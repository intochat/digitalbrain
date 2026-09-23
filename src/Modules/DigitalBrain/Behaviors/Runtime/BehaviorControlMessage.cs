namespace DigitalBrain.Core;

public sealed record BehaviorControlMessage(int Version, Guid GenerationId, long Sequence, string Kind, bool Ready = false, string? Token = null);