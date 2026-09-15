using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Time;

[assembly: NeuronJsonContext(typeof(TimeJson))]

namespace DigitalBrain.Time;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TimerGeneration))]
[JsonSerializable(typeof(ScheduleTimer))]
[JsonSerializable(typeof(ScheduleTimerAt))]
[JsonSerializable(typeof(StopTimer))]
[JsonSerializable(typeof(TimerSnapshot))]
[JsonSerializable(typeof(TimerStatus))]
[JsonSerializable(typeof(TimerResolution))]
[JsonSerializable(typeof(SchedulingBody))]
[JsonSerializable(typeof(TimerElapsedBody))]
[JsonSerializable(typeof(Accepted<TimerGeneration>))]
[JsonSerializable(typeof(SetReminder))]
[JsonSerializable(typeof(CancelReminder))]
[JsonSerializable(typeof(ReminderKey))]
[JsonSerializable(typeof(ReminderDue))]
[JsonSerializable(typeof(ReminderRejected))]
[JsonSerializable(typeof(ReminderScheduling))]
[JsonSerializable(typeof(ReminderItem))]
[JsonSerializable(typeof(RemindersSnapshot))]
[JsonSerializable(typeof(Accepted<ReminderKey>))]
public sealed partial class TimeJson : JsonSerializerContext;
