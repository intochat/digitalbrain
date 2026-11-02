using DigitalBrain.Spi;
using DigitalBrain.Testing.Tests.Unit.Apps;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

public sealed class TestBrainFacts
{
    private static readonly BrainId Brain = new("ada");
    private static readonly NeuronId Source = new(Brain, "rx.source", "s");
    private static readonly NeuronId Sink = new(Brain, "rx.sink", "k");

    [Fact]
    public async Task TestBrainLocalIsTestOSLocal()
    {
        var viaOS = await Heard(configure => TestOS.Local(configure));
        var viaBrain = await Heard(configure => TestBrain.Local(configure));

        Assert.Equal(["n:7"], viaOS);
        Assert.Equal(viaOS, viaBrain);
        Assert.Equal(TestOS.Local().GetType(), TestBrain.Local().GetType());
    }

    [Fact]
    public void TestBrainDefinitionOfIsTestOSDefinitionOf()
        => Assert.Equal(TestOS.DefinitionOf(typeof(NeedsClockApp)).Id, TestBrain.DefinitionOf(typeof(NeedsClockApp)).Id);

    private static async Task<IReadOnlyList<string>> Heard(Func<Action<TestOSOptions>, TestOS> local)
    {
        var probe = new Probe();
        await using var os = local(options => options.Services = probe).Register(typeof(NumberSink).Assembly);
        await os.Get<INumberSink>(Sink).Subscribe(Source);
        await os.Get<INumberSource>(Source).Emit(7);
        await os.Idle();
        return probe.Log;
    }
}
