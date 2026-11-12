using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using DigitalBrain.Units;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

public sealed class AlcCollectibleUnloadFacts
{
    private const string BudgetSource = """
        namespace Me.TestAlc;

        [Signal("alc.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [State("alc.week")]
        public sealed record WeekSpend(decimal Spent = 0)
        {
            public WeekSpend() : this(0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "alc.week")]
            public static WeekSpend WeeklyCap(Charging s, WeekSpend t) =>
                t with { Spent = t.Spent + s.Amount };
        }
        """;

    private static SynapseBuild CompileSource(string key = "Me.TestAlc")
    {
        var source = new SynapseSource(key, new Dictionary<string, string> { ["Budget.cs"] = BudgetSource }, null);
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());
        var build = compiler.Compile(source);
        if (!build.Success)
        {
            throw new InvalidOperationException($"Compilation failed: {string.Join("\n", build.Diagnostics)}");
        }
        return build;
    }

    [Fact]
    public async Task InspectStateUsingOptionsFor_AllowsCollectibleAlcToUnloadCleanly()
    {
        var loader = new UnitLoader();
        var build = CompileSource("Me.TestAlcClean");

        var (weakAlc, inspectedJson) = RunInspectAndTriggerUnload(loader, build);

        // Verify state was inspected accurately via IStateInspectable
        Assert.NotNull(inspectedJson);
        Assert.Equal(42m, inspectedJson.Value.GetProperty("spent").GetDecimal());

        for (var i = 0; i < 10 && !IsDead(weakAlc); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(IsDead(weakAlc), "The collectible ALC must unload cleanly after IStateInspectable.InspectState was invoked.");
    }

    [Fact]
    public async Task SerializingThroughRuntimeProtocolJson_PinsCollectibleAlc_PreventingCleanUnload()
    {
        var loader = new UnitLoader();
        var build = CompileSource("Me.TestAlcPin");

        var weakAlc = RunPoisonousSerializeAndTriggerUnload(loader, build);

        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // The ALC is pinned in the process-wide RuntimeProtocol.Json cache, so unload MUST fail!
        Assert.False(IsDead(weakAlc), "WeakReference to AssemblyLoadContext must still be alive (pinned).");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsDead(WeakReference<AssemblyLoadContext> weak) => !weak.TryGetTarget(out _);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<AssemblyLoadContext> WeakAlc, JsonElement? InspectedJson) RunInspectAndTriggerUnload(UnitLoader loader, SynapseBuild build)
    {
        var image = new BuildImage(new BuildRef("Me.TestAlcClean", build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
        var handle = loader.Acquire(image);
        var weakAlc = GetWeak(handle);

        var stateType = handle.Catalog.States["alc.week"];
        var neuronType = typeof(UnitNeuron<>).MakeGenericType(stateType);
        var catalogSource = new SynapseCatalogSource(SynapseCatalog.Scan(), loader);
        var slotResolver = new UnboundSlotResolver();

        var neuron = (Neuron)Activator.CreateInstance(neuronType, catalogSource, slotResolver)!;

        var neuronId = new NeuronId(new BrainId("alice"), "alc.week", "default");
        var context = new FakeNeuronContext(neuronId);
        NeuronAttachment.Attach(neuron, context);

        // Prime state with Spent = 42
        var storedType = typeof(StoredUnit<>).MakeGenericType(stateType);
        var initialCustomState = Activator.CreateInstance(stateType, 42m)!;
        var storedUnit = Activator.CreateInstance(storedType, initialCustomState, null)!;
        context.MemoryState.Save(storedUnit, []).GetAwaiter().GetResult();
        NeuronAttachment.Activate(neuron, CancellationToken.None).GetAwaiter().GetResult();

        // Inspect state using IStateInspectable (which internally calls RuntimeProtocol.OptionsFor)
        var inspectable = (IStateInspectable)neuron;
        var element = inspectable.InspectState(CancellationToken.None).GetAwaiter().GetResult();

        NeuronAttachment.Deactivate(neuron, DeactivationCause.Shutdown, CancellationToken.None).GetAwaiter().GetResult();
        context.MemoryState.Clear().GetAwaiter().GetResult();

        // Trigger unload
        handle.Dispose();
        return (weakAlc, element);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AssemblyLoadContext> GetWeak(UnitHandle handle)
        => new(AssemblyLoadContext.GetLoadContext(handle.Catalog.States["alc.week"].Assembly)!);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AssemblyLoadContext> RunPoisonousSerializeAndTriggerUnload(UnitLoader loader, SynapseBuild build)
    {
        var image = new BuildImage(new BuildRef("Me.TestAlcPin", build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
        var handle = loader.Acquire(image);
        var weakAlc = GetWeak(handle);

        var stateType = handle.Catalog.States["alc.week"];
        var initialCustomState = Activator.CreateInstance(stateType, 99m)!;

        // Deliberately serialize using the global static RuntimeProtocol.Json instead of OptionsFor(stateType)
        _ = JsonSerializer.SerializeToElement(initialCustomState, stateType, RuntimeProtocol.Json);

        handle.Dispose();
        return weakAlc;
    }
}
