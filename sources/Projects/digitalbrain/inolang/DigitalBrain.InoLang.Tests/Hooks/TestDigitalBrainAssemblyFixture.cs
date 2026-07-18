using DigitalBrain.InoLang.Tests;
using DigitalBrain.InoLang.Tests.Hooks;

[assembly: AssemblyFixture(typeof(TestDigitalBrainAssemblyFixture))]

namespace DigitalBrain.InoLang.Tests.Hooks;

public sealed class TestDigitalBrainAssemblyFixture : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => TestDigitalBrain.ShutdownIfBootedAsync();
}
