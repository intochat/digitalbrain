using System.Reflection.Emit;
using System.Runtime.Loader;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests.Unit.Screens;

public sealed record FirstSignal : Signal;

[Signal("screen.second")]
public sealed record SecondSignal : Signal;

public sealed class DummyScreenState { }

public class DummyScreen : Screen<DummyScreenState>
{
    protected override View Render(DummyScreenState state) => new("Dummy", new Column([]));

    [On]
    public Task React(FirstSignal signal) { _ = State; return Task.CompletedTask; }

    [On]
    public Task React(SecondSignal signal) { _ = State; return Task.CompletedTask; }
}

public sealed class ScreenSignalsFacts
{
    [Fact]
    public void For_DiscoversAllOnMethodsByJournalAlias()
    {
        var map = ScreenSignals.For(typeof(DummyScreen));
        Assert.NotNull(map);
        Assert.True(map.ContainsKey(SignalAliases.Of(typeof(FirstSignal))));
        Assert.True(map.ContainsKey("screen.second"));
        Assert.Equal(typeof(FirstSignal), map[SignalAliases.Of(typeof(FirstSignal))]);
        Assert.Equal(typeof(SecondSignal), map["screen.second"]);
    }

    [Fact]
    public void Find_ReturnsSignalTypeForKnownAlias_AndNullForUnknown()
    {
        var type = ScreenSignals.Find(typeof(DummyScreen), "screen.second");
        Assert.Equal(typeof(SecondSignal), type);

        var unknown = ScreenSignals.Find(typeof(DummyScreen), "unknown.alias");
        Assert.Null(unknown);
    }

    [Fact]
    public void CollectibleScreen_ScansWithoutPinningStaticCache()
    {
        var alc = new AssemblyLoadContext("CollectibleContext", isCollectible: true);
        try
        {
            using var stream = File.OpenRead(typeof(DummyScreen).Assembly.Location);
            var loadedAssembly = alc.LoadFromStream(stream);
            var loadedType = loadedAssembly.GetType(typeof(DummyScreen).FullName!)!;

            Assert.True(loadedType.IsCollectible);

            var found = ScreenSignals.Find(loadedType, "screen.second");
            Assert.NotNull(found);
            Assert.Equal(nameof(SecondSignal), found.Name);
        }
        finally
        {
            alc.Unload();
        }
    }
}
