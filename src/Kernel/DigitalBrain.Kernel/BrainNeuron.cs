using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record BrainNeuron(string Id, string GrainType, string Identity, string Placement);

