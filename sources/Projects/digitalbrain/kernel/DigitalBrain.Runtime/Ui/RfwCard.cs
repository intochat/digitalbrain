using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Ui;

// RfwCard carries a Remote Flutter Widgets payload from a neuron to the
// Flutter Home feed. The Orleans wire format stays small because DataJson
// is an opaque string blob — RFW data is dynamic, so Orleans has no static
// schema to serialize. The Flutter side parses DataJson into rfw.DynamicContent.
[GenerateSerializer]
public sealed record RfwCard([property: Id(1)] string LibraryName,
    [property: Id(2)] string RootWidget,
    [property: Id(3)] string DataJson
) : Synapse;
