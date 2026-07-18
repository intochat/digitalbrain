using Ino.Core;

namespace Ino.Testing;

public sealed record CaptureEntry(Type GrainType, Type SynapseType, ISynapse Payload, DateTimeOffset At);
