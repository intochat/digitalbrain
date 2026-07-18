using DigitalBrain.Runtime.Neurons;

// Widget-Canvas demo contracts. FQNs are pinned to DigitalBrain.WidgetCanvas.*
// so the authored .ino neurons (samples/widget-canvas/*.ino) bind to them by
// name via `using x = synapse(DigitalBrain.WidgetCanvas.X)`. Discovered and
// registered by FullName through SynapsePayloadRegistry.RegisterDiscoveredSynapses.
namespace DigitalBrain.WidgetCanvas;

[GenerateSerializer]
public sealed record SetClock(
    [property: Id(1)] string Timezone
) : Synapse;

[GenerateSerializer]
public sealed record ClockShown(
    [property: Id(1)] string Timezone
) : Synapse;

[GenerateSerializer]
public sealed record RemindMe(
    [property: Id(1)] int Minutes
) : Synapse;

[GenerateSerializer]
public sealed record Snooze(
    [property: Id(1)] int Minutes
) : Synapse;

[GenerateSerializer]
public sealed record ReminderArmed(
    [property: Id(1)] int Minutes
) : Synapse;

[GenerateSerializer]
public sealed record Fired(
    [property: Id(1)] string Text
) : Synapse;

[GenerateSerializer]
public sealed record ShowFlight(
    [property: Id(1)] string Code
) : Synapse;

[GenerateSerializer]
public sealed record FlightShown(
    [property: Id(1)] string Code
) : Synapse;
