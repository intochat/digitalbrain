using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record GraphEvent(
    long Sequence,
    string Kind,
    Guid ConnectionId,
    string? Source,
    string? SynapseAlias,
    string? Target,
    DateTimeOffset Timestamp);

