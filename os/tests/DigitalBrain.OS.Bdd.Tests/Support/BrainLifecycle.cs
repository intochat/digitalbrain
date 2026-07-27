using Reqnroll;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public sealed class BrainLifecycle(BrainWorld world)
{
    [AfterScenario]
    public Task ReleaseBrainAsync() => world.CloseAsync();
}
