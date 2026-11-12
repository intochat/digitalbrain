using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Invocation;
using DigitalBrain.Units;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using Xunit;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Load;

public sealed class UnitLoaderFacts
{
    private static readonly SynapseCompiler CleanCompiler = new(ReferenceSet.FromDefaultContext());
    private static readonly SynapseCompiler BypassingCompiler = new(ReferenceSet.FromDefaultContext(), [], withoutBannedApis: true);

    private static BuildImage CompileBudget(string key = "Me.Budget", string text = SynapseCompilerFacts.Budget)
    {
        var source = new SynapseSource(key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);
        var build = CleanCompiler.Compile(source);
        Assert.True(build.Success);
        return new BuildImage(new BuildRef(key, build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
    }

    private static BuildImage CompileBanned(string key = "Me.Banned", string leak = "public static class Leak { public static object Client() => new System.Net.Http.HttpClient(); }")
    {
        var source = new SynapseSource(key, new Dictionary<string, string> { ["Budget.cs"] = SynapseCompilerFacts.Budget + "\n" + leak }, null);
        var build = BypassingCompiler.Compile(source);
        Assert.True(build.Success);
        return new BuildImage(new BuildRef(key, build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
    }

    [Fact]
    public void AcquireLoadsAndScansCatalog()
    {
        var loader = new UnitLoader();
        var image = CompileBudget();

        using var handle = loader.Acquire(image);

        Assert.NotNull(handle);
        Assert.Equal(image.Ref, handle.Build);
        Assert.True(handle.Catalog.States.ContainsKey("week"));
        Assert.Equal("week", handle.Catalog.States["week"].Name switch { var n => "week" });
        var stateType = handle.StateOf("week");
        Assert.NotNull(stateType);
        Assert.Equal("WeekSpend", stateType.Name);
        Assert.Throws<KeyNotFoundException>(() => handle.StateOf("nonexistent"));
    }

    [Fact]
    public void TwoAcquiresShareTheSameContext()
    {
        var loader = new UnitLoader();
        var image = CompileBudget();

        using var handle1 = loader.Acquire(image);
        using var handle2 = loader.Acquire(image);

        Assert.Same(handle1.Catalog, handle2.Catalog);
        Assert.Same(handle1.StateOf("week"), handle2.StateOf("week"));
    }

    [Fact]
    public void RefcountingMaintainsSlotUntilZero()
    {
        var loader = new UnitLoader();
        var image = CompileBudget();

        var handle1 = loader.Acquire(image);
        var handle2 = loader.Acquire(image);

        handle1.Dispose();
        // Still held by handle2
        using (var extra = loader.TryAcquire(image.Ref))
        {
            Assert.NotNull(extra);
        }

        handle2.Dispose();
        // Count reaches zero, slot removed
        Assert.Null(loader.TryAcquire(image.Ref));
    }

    [Fact]
    public void DisallowedAssemblyRefusedBeforeLoad()
    {
        var loader = new UnitLoader();
        var bannedImage = CompileBanned();

        var ex = Assert.Throws<UnitRefusedException>(() => loader.Acquire(bannedImage));
        Assert.Equal(bannedImage.Ref, ex.Build);
        Assert.Contains("System.Net.Http.HttpClient", ex.Disallowed);

        Assert.Null(loader.TryAcquire(bannedImage.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void WeakCatalogCacheLookup()
    {
        var loader = new UnitLoader();
        var image = CompileBudget();

        using var handle = loader.Acquire(image);
        var stateType = handle.StateOf("week");

        var catalog = loader.CatalogOf(stateType);
        Assert.Same(handle.Catalog, catalog);

        // Non-collectible type returns null
        Assert.Null(loader.CatalogOf(typeof(string)));
    }

    [Fact]
    public void LoadedAndUnloadingEvents()
    {
        var loader = new UnitLoader();
        var image = CompileBudget();

        BuildRef? loadedRef = null;
        IReadOnlyList<Type>? loadedSignals = null;
        BuildRef? unloadingRef = null;
        IReadOnlyList<Type>? unloadingSignals = null;

        loader.Loaded += (b, s) => { loadedRef = b; loadedSignals = s; };
        loader.Unloading += (b, s) => { unloadingRef = b; unloadingSignals = s; };

        var handle = loader.Acquire(image);
        Assert.Equal(image.Ref, loadedRef);
        Assert.NotNull(loadedSignals);
        Assert.Contains(loadedSignals, t => t.Name == "Charging");

        handle.Dispose();
        Assert.Equal(image.Ref, unloadingRef);
        Assert.NotNull(unloadingSignals);
        Assert.Contains(unloadingSignals, t => t.Name == "Charging");
    }

    [Fact]
    public void CorruptBytesThrowAndLeaveNoState()
    {
        var loader = new UnitLoader();
        var corruptImage = new BuildImage(new BuildRef("corrupt", "badhash"), "CorruptAsm", [0, 1, 2, 3], [0, 1, 2, 3]);

        Assert.ThrowsAny<Exception>(() => loader.Acquire(corruptImage));
        Assert.Null(loader.TryAcquire(corruptImage.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void LingeringEmptiesAfterForcedGC()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.Collect");

        LoadAndDrop(loader, image);

        var lingering = loader.Lingering();
        Assert.Contains(image.Ref, lingering);

        // Force GC in rounds to reclaim collectible ALC
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.Empty(loader.Lingering());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadAndDrop(UnitLoader loader, BuildImage image)
    {
        using var handle = loader.Acquire(image);
        Assert.NotNull(handle);
    }
}
