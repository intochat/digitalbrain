using Reqnroll;

namespace DigitalBrain.Os.Bdd.Tests;

[Binding]
public sealed class BrainLifecycle(BrainWorld world)
{
    [AfterScenario]
    public Task ReleaseBrainAsync() => world.CloseAsync();
}
