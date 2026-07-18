using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GenerateSerializer]
public sealed record InspectWidgetTreeRequest([property: Id(1)] string? TargetWidgetType = null
) : Synapse;

[GenerateSerializer]
public sealed record WidgetTreeResponse([property: Id(1)] bool Success,
    [property: Id(2)] string WidgetTreeXmlOrJson,
    [property: Id(3)] string? ErrorMessage = null
) : Synapse;
