using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record BrainTopologySnapshot(
    IReadOnlyList<BrainModule> Modules,
    IReadOnlyList<BrainNeuron> Neurons,
    DateTimeOffset ObservedAt,
    IReadOnlyList<BrainConnection> Connections,
    IReadOnlyList<BrainBroadcastRoute> BroadcastRoutes);

