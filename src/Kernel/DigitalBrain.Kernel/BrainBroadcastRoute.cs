using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record BrainBroadcastRoute(string SynapseAlias, string HandlerGrainType);

