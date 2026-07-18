using Ino.Core;

namespace Ino.NeuronTesting;

public sealed record ChatFrame(
    CorrelationId CorrelationId,
    string ContentType,
    bool IsSkeleton,
    string Reply,
    RfwSnapshot? Rfw);
