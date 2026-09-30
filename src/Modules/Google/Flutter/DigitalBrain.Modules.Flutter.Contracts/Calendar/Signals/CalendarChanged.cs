using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Calendar.Signals;

[GenerateSerializer, Alias("ui.calendar-changed")]
public sealed record CalendarChanged([property: Id(0)] string Name, [property: Id(1)] int Version) : Signal;
