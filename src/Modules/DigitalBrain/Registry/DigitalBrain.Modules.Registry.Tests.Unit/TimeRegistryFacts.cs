using DigitalBrain.Registry;
using DigitalBrain.Qdrant;
using DigitalBrain.Time;
using DigitalBrain.Time.Reminders;
using DigitalBrain.Time.Timers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class TimeRegistryFacts
{
    [Fact]
    public async Task SelectedTimeModuleDiscoversItsContracts()
    {
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>()
            .WithModule<TimeModule>().WithReminders()
            .StartAsync(TestContext.Current.CancellationToken);
        var registry = brain.SiloServices.GetRequiredService<NeuronRegistry>();

        Assert.Equal(typeof(DigitalBrain.Time.Timers.ITimer), registry.Find("timer")?.Interface);
        Assert.Equal(typeof(IReminder), registry.Find("reminder")?.Interface);
    }
}