using System.Runtime.CompilerServices;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Load;

public sealed class SynapseLoaderFacts
{
    private static Type? s_held;

    private static SynapseBuild Build() =>
        new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(
            new SynapseSource("Me.Budget", new Dictionary<string, string> { ["Budget.cs"] = SynapseCompilerFacts.Budget }, null));

    [Fact]
    public void LoadScansStatesSynapsesAndSignals()
    {
        var build = Build();
        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        var loader = new SynapseLoader();

        var loaded = loader.Load(build);

        Assert.Equal("Me.Budget", loaded.SourceKey);
        Assert.Equal(build.Hash, loaded.Hash);
        Assert.Equal("week", Assert.Single(loaded.Catalog.States).Key);
        Assert.Equal(3, loaded.Catalog.Synapses.Count);
        Assert.Equal(["Charging", "WeekRolled"], loaded.Signals.Select(t => t.Name).Order().ToArray());
        Assert.Equal("WeekSpend", Assert.Single(loaded.States).Name);
    }

    [Fact]
    public void LoadRefusesAFailedBuild()
    {
        var failed = new SynapseBuild(false, [], [], [], "h", "synapse.Me.Budget.h");

        Assert.Throws<InvalidOperationException>(() => new SynapseLoader().Load(failed));
    }

    [Fact]
    public void TypesResolveFrameworkTypesFromTheDefaultContext()
    {
        var loaded = new SynapseLoader().Load(Build());

        var gate = loaded.Catalog.Synapses.Select(d => d.Function).First(m => m.Name == "WeeklyCap");
        Assert.Same(typeof(Check), gate.ReturnType);
        Assert.Equal("Charging", Assert.Single(loaded.Signals, t => t.Name == "Charging").Name);
        Assert.All(loaded.Signals, t => Assert.True(typeof(Signal).IsAssignableFrom(t)));
    }

    [Fact]
    public async Task UnloadFreesTheContextWhenNothingHoldsIt()
    {
        var loader = new SynapseLoader();

        Assert.True(await LoadAndUnload(loader, hold: false));
    }

    [Fact]
    public async Task UnloadReportsALeakWhenATypeIsHeld()
    {
        var loader = new SynapseLoader();
        var build = Build();

        Assert.False(await LoadAndUnload(loader, hold: true, build));

        s_held = null;
        Assert.True(await loader.RetryUnload("Me.Budget", build.Hash));
    }

    [Fact]
    public async Task ALeakSurvivesEveryRoundAndTheRetryStillCollects()
    {
        var loader = new SynapseLoader();
        var build = Build();

        Assert.False(await LoadAndUnload(loader, hold: true, build, gcRounds: 3));

        s_held = null;
        Assert.True(await loader.RetryUnload("Me.Budget", build.Hash));
    }

    [Fact]
    public void LoadRefusesTheSameSourceAndHashTwice()
    {
        var loader = new SynapseLoader();
        var build = Build();
        var first = loader.Load(build);

        Assert.Throws<InvalidOperationException>(() => loader.Load(build));
        GC.KeepAlive(first);
    }

    [Fact]
    public async Task CorruptBytesFailToLoadAndRegisterNothing()
    {
        var loader = new SynapseLoader();
        var good = Build();
        var corrupt = good with { Assembly = [1, 2, 3, 4], Symbols = [] };

        Assert.ThrowsAny<BadImageFormatException>(() => loader.Load(corrupt));

        Assert.True(await loader.RetryUnload("Me.Budget", good.Hash));
        GC.KeepAlive(loader.Load(good));
    }

    // Loads and unloads in a frame of its own; it returns the pending task, never the record.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<bool> LoadAndUnload(SynapseLoader loader, bool hold, SynapseBuild? build = null, int gcRounds = 10)
    {
        var loaded = loader.Load(build ?? Build());
        if (hold)
        {
            s_held = loaded.Signals[0];
            Assert.NotNull(s_held);
        }

        return loader.Unload(loaded, gcRounds);
    }
}
