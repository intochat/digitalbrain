using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Invocation;
using DigitalBrain.Units;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;
using Xunit;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Load;

public sealed class UnitLoaderAdversarialFacts
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

    private static BuildImage CompileBanned(string key, string leak)
    {
        var source = new SynapseSource(key, new Dictionary<string, string> { ["Budget.cs"] = SynapseCompilerFacts.Budget + "\n" + leak }, null);
        var build = BypassingCompiler.Compile(source);
        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        return new BuildImage(new BuildRef(key, build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
    }

    [Fact]
    public async Task ConcurrentAcquires_ShareSingleSlotAndDeduplicateALCLoad()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.Concurrent");

        var loadedCount = 0;
        var unloadingCount = 0;
        loader.Loaded += (_, _) => Interlocked.Increment(ref loadedCount);
        loader.Unloading += (_, _) => Interlocked.Increment(ref unloadingCount);

        const int concurrency = 32;
        var handles = new UnitHandle[concurrency];

        // 32 threads race to Acquire the exact same image
        await Parallel.ForAsync(0, concurrency, async (i, _) =>
        {
            await Task.Yield();
            handles[i] = loader.Acquire(image);
        });

        // Exactly one load must have occurred
        Assert.Equal(1, loadedCount);
        Assert.Equal(0, unloadingCount);

        var firstHandle = handles[0];
        Assert.NotNull(firstHandle);
        for (var i = 1; i < concurrency; i++)
        {
            Assert.Same(firstHandle.Catalog, handles[i].Catalog);
            Assert.Same(firstHandle.StateOf("week"), handles[i].StateOf("week"));
            Assert.Same(firstHandle.StateOf("week").Assembly, handles[i].StateOf("week").Assembly);
        }

        // 32 threads race to dispose their handles
        await Parallel.ForAsync(0, concurrency, async (i, _) =>
        {
            await Task.Yield();
            handles[i].Dispose();
        });

        // Exactly one unload must have occurred when the refcount reached zero
        Assert.Equal(1, unloadingCount);
        Assert.Null(loader.TryAcquire(image.Ref));
    }

    [Fact]
    public async Task DoubleDisposeAndConcurrentDispose_IsStrictlyIdempotent()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.IdempotentDispose");

        var unloadingCount = 0;
        loader.Unloading += (_, _) => Interlocked.Increment(ref unloadingCount);

        var handle1 = loader.Acquire(image);
        var handle2 = loader.Acquire(image);

        // 50 concurrent calls to Dispose on handle1
        await Parallel.ForAsync(0, 50, async (_, _) =>
        {
            await Task.Yield();
            handle1.Dispose();
        });

        // Slot must still be alive because handle2 is retained!
        Assert.Equal(0, unloadingCount);
        using (var extra = loader.TryAcquire(image.Ref))
        {
            Assert.NotNull(extra);
        }

        // 50 concurrent calls to Dispose on handle2
        await Parallel.ForAsync(0, 50, async (_, _) =>
        {
            await Task.Yield();
            handle2.Dispose();
        });

        // Now refcount reached zero: exactly one unload notification
        Assert.Equal(1, unloadingCount);
        Assert.Null(loader.TryAcquire(image.Ref));
    }

    [Fact]
    public void ReacquireAfterUnload_CreatesFreshSlotAndContext()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.Reacquire");

        var loadedCount = 0;
        var unloadingCount = 0;
        loader.Loaded += (_, _) => loadedCount++;
        loader.Unloading += (_, _) => unloadingCount++;

        // First lifecycle
        var handle1 = loader.Acquire(image);
        var asm1 = handle1.StateOf("week").Assembly;
        handle1.Dispose();

        Assert.Equal(1, loadedCount);
        Assert.Equal(1, unloadingCount);
        Assert.Null(loader.TryAcquire(image.Ref));

        // Reacquire fresh
        var handle2 = loader.Acquire(image);
        var asm2 = handle2.StateOf("week").Assembly;

        Assert.Equal(2, loadedCount);
        Assert.NotSame(asm1, asm2); // Fresh ALC loaded!

        handle2.Dispose();
        Assert.Equal(2, unloadingCount);
        Assert.Null(loader.TryAcquire(image.Ref));
    }

    [Fact]
    public void DisallowedReferences_FileAccess_RefusedBeforeLoad()
    {
        var loader = new UnitLoader();
        var image = CompileBanned("Me.BannedFile", "public static class Leak { public static string Read() => System.IO.File.ReadAllText(\"path\"); }");

        var ex = Assert.Throws<UnitRefusedException>(() => loader.Acquire(image));
        Assert.Equal(image.Ref, ex.Build);
        Assert.Contains("File", ex.Disallowed);

        Assert.Null(loader.TryAcquire(image.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void DisallowedReferences_ReflectionAssembly_RefusedBeforeLoad()
    {
        var loader = new UnitLoader();
        var image = CompileBanned("Me.BannedReflection", "public static class Leak { public static object Asm() => System.Reflection.Assembly.GetExecutingAssembly(); }");

        var ex = Assert.Throws<UnitRefusedException>(() => loader.Acquire(image));
        Assert.Equal(image.Ref, ex.Build);
        Assert.Contains("Assembly", ex.Disallowed);

        Assert.Null(loader.TryAcquire(image.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void DisallowedReferences_EnvironmentAccess_RefusedBeforeLoad()
    {
        var loader = new UnitLoader();
        var image = CompileBanned("Me.BannedEnv", "public static class Leak { public static string? Env() => System.Environment.GetEnvironmentVariable(\"PATH\"); }");

        var ex = Assert.Throws<UnitRefusedException>(() => loader.Acquire(image));
        Assert.Equal(image.Ref, ex.Build);
        Assert.Contains("Environment", ex.Disallowed);

        Assert.Null(loader.TryAcquire(image.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void DisallowedReferences_ThreadAccess_RefusedBeforeLoad()
    {
        var loader = new UnitLoader();
        var image = CompileBanned("Me.BannedThread", "public static class Leak { public static void Sleep() => System.Threading.Thread.Sleep(10); }");

        var ex = Assert.Throws<UnitRefusedException>(() => loader.Acquire(image));
        Assert.Equal(image.Ref, ex.Build);
        Assert.Contains("Thread", ex.Disallowed);

        Assert.Null(loader.TryAcquire(image.Ref));
        Assert.Empty(loader.Lingering());
    }

    [Fact]
    public void MultiBrainALCSharing_SimulatedMultiBrainAcquisition()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.MultiBrain");

        // Simulate Brain 1, Brain 2, Brain 3 acquiring the build
        var brain1Handle = loader.Acquire(image);
        var brain2Handle = loader.Acquire(image);
        var brain3Handle = loader.Acquire(image);

        // All 3 brains share identical Catalog and Type
        Assert.Same(brain1Handle.Catalog, brain2Handle.Catalog);
        Assert.Same(brain2Handle.Catalog, brain3Handle.Catalog);

        var t1 = brain1Handle.StateOf("week");
        var t2 = brain2Handle.StateOf("week");
        var t3 = brain3Handle.StateOf("week");
        Assert.Same(t1, t2);
        Assert.Same(t2, t3);

        // Brain 1 unloads its handle
        brain1Handle.Dispose();
        using (var extra1 = loader.TryAcquire(image.Ref))
        {
            Assert.NotNull(extra1);
        }

        // Brain 2 unloads its handle
        brain2Handle.Dispose();
        using (var extra2 = loader.TryAcquire(image.Ref))
        {
            Assert.NotNull(extra2);
        }

        // Brain 3 unloads its handle -> refcount hits 0
        brain3Handle.Dispose();
        Assert.Null(loader.TryAcquire(image.Ref));
    }

    [Fact]
    public void ZeroTenantLeakage_StaticAndReflectionAudit()
    {
        var targetTypes = new[]
        {
            typeof(UnitLoader),
            typeof(IUnitLoader),
            typeof(UnitHandle),
            typeof(SynapseLoadContext),
            typeof(AllowedReferences),
        };

        foreach (var type in targetTypes)
        {
            // Check methods
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("Brain", method.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Tenant", method.Name, StringComparison.OrdinalIgnoreCase);

                Assert.DoesNotContain("BrainId", method.ReturnType.Name, StringComparison.OrdinalIgnoreCase);
                foreach (var param in method.GetParameters())
                {
                    Assert.DoesNotContain("BrainId", param.ParameterType.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Brain", param.Name ?? "", StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Tenant", param.Name ?? "", StringComparison.OrdinalIgnoreCase);
                }
            }

            // Check constructors
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var param in ctor.GetParameters())
                {
                    Assert.DoesNotContain("BrainId", param.ParameterType.Name, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Check fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("BrainId", field.FieldType.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Brain", field.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Tenant", field.Name, StringComparison.OrdinalIgnoreCase);
            }

            // Check properties
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain("BrainId", prop.PropertyType.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Brain", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Tenant", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void WeakCatalogCache_DoesNotPinCollectibleALC_EvenAfterCatalogScan()
    {
        var loader = new UnitLoader();
        var image = CompileBudget("Me.AdversarialCollect");

        RunScopedAcquisition(loader, image, out var stateTypeWeakRef);

        // Confirm ALC is lingering
        var lingering = loader.Lingering();
        Assert.Contains(image.Ref, lingering);

        // Force GC in rounds
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // Lingering must now be empty
        Assert.Empty(loader.Lingering());

        // Weak reference to Type must be dead or if queried CatalogOf should return null
        if (stateTypeWeakRef.TryGetTarget(out var collectedType))
        {
            Assert.Null(loader.CatalogOf(collectedType));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunScopedAcquisition(UnitLoader loader, BuildImage image, out WeakReference<Type> stateTypeWeakRef)
    {
        using var handle = loader.Acquire(image);
        var stateType = handle.StateOf("week");
        stateTypeWeakRef = new WeakReference<Type>(stateType);

        var catalog = loader.CatalogOf(stateType);
        Assert.NotNull(catalog);
        Assert.Same(handle.Catalog, catalog);
    }
}
