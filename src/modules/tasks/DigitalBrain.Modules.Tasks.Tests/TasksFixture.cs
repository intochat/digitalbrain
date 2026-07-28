using DigitalBrain.Testing;

namespace DigitalBrain.Tasks.Tests;

public sealed class TasksFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<TasksModule>();
        brain.AddModule<TasksHarnessModule>();
    }
}
