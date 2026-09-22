using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Calendar;

[Alias("calendar"), Orleans.Metadata.DefaultGrainType(UIVocabulary.CalendarType)]
public interface ICalendar : INeuron
{
    Task Set(string mode, IReadOnlyList<string> selected);
    [ReadOnly, Alias("read")] Task<CalendarState> Read();
}

[GenerateSerializer, Alias("ui.calendar-state")]
public sealed class CalendarState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Mode { get; set; } = "day";
    [Id(3)] public List<string> Selected { get; set; } = [];
}