using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record BrainConnection(
    Guid ConnectionId,
    string Source,
    string SynapseAlias,
    string Target,
    string? Transform,
    DateTimeOffset? ExpiresAt);

