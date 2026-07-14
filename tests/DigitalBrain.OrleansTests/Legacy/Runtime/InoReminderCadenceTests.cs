using System.Reflection;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class InoReminderCadenceTests
{
    [Fact]
    public void Durable_reminder_periods_respect_the_Orleans_runtime_minimum()
    {
        var minimum = new ReminderOptions().MinimumReminderPeriod;
        Assert.Equal(TimeSpan.FromMinutes(1), minimum);

        AssertPeriodAtLeast<ConversationNeuron>("OperationReminderPeriod", minimum);
        AssertPeriodAtLeast<InoOperationWorkerGrain>("ReminderPeriod", minimum);
        AssertPeriodAtLeast<InoConversationOutboxDispatcherGrain>("ReminderPeriod", minimum);
    }

    [Fact]
    public void Prompt_execution_uses_activation_timers_while_durable_recovery_stays_on_the_minimum_cadence()
    {
        var minimum = new ReminderOptions().MinimumReminderPeriod;

        AssertPeriodAtLeast<ConversationNeuron>("OperationReminderDueTime", minimum);
        AssertPeriodAtLeast<InoOperationWorkerGrain>("ReminderDueTime", minimum);
        AssertPeriodAtLeast<InoConversationOutboxDispatcherGrain>("ReminderDueTime", minimum);
        AssertTimerField<ConversationNeuron>("_operationTimer");
        AssertTimerField<InoOperationWorkerGrain>("_timer");
        AssertTimerField<InoConversationOutboxDispatcherGrain>("_timer");
    }

    private static void AssertPeriodAtLeast<T>(string fieldName, TimeSpan minimum)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        var period = Assert.IsType<TimeSpan>(field?.GetValue(null));
        Assert.True(
            period >= minimum,
            $"{typeof(T).Name}.{fieldName} ({period}) must be at least the Orleans minimum ({minimum}).");
    }

    private static void AssertTimerField<T>(string fieldName)
    {
        var field = typeof(T).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Equal(typeof(IGrainTimer), field?.FieldType);
    }
}
