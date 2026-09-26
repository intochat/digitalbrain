using DigitalBrain.Core;
using DigitalBrain.Time;
using DigitalBrain.Time.Reminders;
using DigitalBrain.Time.Timers;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TimeRegistryFacts
{
    [Fact]
    public void SelectedTimeModuleContributesStableNeuronContracts()
    {
        var registry = new BrainCompositionBuilder().WithModule<TimeModule>().Build().NeuronRegistry;

        Assert.Equal(typeof(ITimer), registry.Find("time.timer")?.ContractType);
        Assert.Equal(typeof(IReminder), registry.Find("time.reminder")?.ContractType);
        Assert.Null(new BrainCompositionBuilder().Build().NeuronRegistry.Find("time.timer"));
    }
}
