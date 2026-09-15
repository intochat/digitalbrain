using DigitalBrain.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Time;

public sealed class TimeModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<TimeOptions>();
        builder.Services.AddSingleton(new BehaviorSourceContract("reminders",
            [new(TimeSignals.ReminderDue, """
                {"type":"object","properties":{"reminderId":{"type":"string"},"text":{"type":"string"},"dueUnixSeconds":{"type":"integer"}},"required":["reminderId","text","dueUnixSeconds"],"additionalProperties":false}
                """), new(TimeSignals.ReminderRejected, """
                {"type":"object","properties":{"reminderId":{"type":"string"},"reason":{"type":"string"}},"required":["reminderId","reason"],"additionalProperties":false}
                """)]));
    }
}
