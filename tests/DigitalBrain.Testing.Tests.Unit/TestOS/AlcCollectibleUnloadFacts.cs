using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
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
        var loader = new SynapseLoader();
        var build = CompileSource("Me.TestAlcClean");

        var (unloadTask, weakAlc, inspectedJson) = await RunInspectAndTriggerUnload(loader, build);

        // Verify state was inspected accurately via IStateInspectable
        Assert.NotNull(inspectedJson);
        Assert.Equal(42m, inspectedJson.Value.GetProperty("spent").GetDecimal());

        // Await the unload and collection task
        var collected = await unloadTask;
        Assert.True(collected, "The collectible ALC must unload cleanly after IStateInspectable.InspectState was invoked.");
        Assert.False(weakAlc.TryGetTarget(out _), "WeakReference to AssemblyLoadContext must be dead.");
    }

    [Fact]
    public async Task SerializingThroughRuntimeProtocolJson_PinsCollectibleAlc_PreventingCleanUnload()
    {
        var loader = new SynapseLoader();
        var build = CompileSource("Me.TestAlcPin");

        var (unloadTask, weakAlc) = RunPoisonousSerializeAndTriggerUnload(loader, build);

        // The ALC is pinned in the process-wide RuntimeProtocol.Json cache, so unload MUST fail!
        var collected = await unloadTask;
        Assert.False(collected, "Serializing a collectible type via static RuntimeProtocol.Json must pin the ALC, preventing unload.");
        Assert.True(weakAlc.TryGetTarget(out _), "WeakReference to AssemblyLoadContext must still be alive (pinned).");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(Task<bool> UnloadTask, WeakReference<AssemblyLoadContext> WeakAlc, JsonElement? InspectedJson)> RunInspectAndTriggerUnload(SynapseLoader loader, SynapseBuild build)
    {
        var loaded = loader.Load(build);
        var alc = AssemblyLoadContext.GetLoadContext(loaded.Assembly)!;
        var weakAlc = new WeakReference<AssemblyLoadContext>(alc);

        var stateType = loaded.States[0];
        var neuronType = typeof(UnitNeuron<>).MakeGenericType(stateType);
        var catalogSource = new SynapseCatalogSource(loaded.Catalog);
        var slotResolver = new UnboundSlotResolver();

        var neuron = (Neuron)Activator.CreateInstance(neuronType, catalogSource, slotResolver)!;

        var neuronId = new NeuronId(new BrainId("alice"), "alc.week", "default");
        var context = new FakeNeuronContext(neuronId);
        NeuronAttachment.Attach(neuron, context);

        // Prime state with Spent = 42
        var storedType = typeof(StoredUnit<>).MakeGenericType(stateType);
        var initialCustomState = Activator.CreateInstance(stateType, 42m)!;
        var storedUnit = Activator.CreateInstance(storedType, initialCustomState, null)!;
        await context.MemoryState.Save(storedUnit, []);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        // Inspect state using IStateInspectable (which internally calls RuntimeProtocol.OptionsFor)
        var inspectable = (IStateInspectable)neuron;
        var element = await inspectable.InspectState(CancellationToken.None);

        // Trigger unload
        var unloadTask = loader.Unload(loaded);
        return (unloadTask, weakAlc, element);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (Task<bool> UnloadTask, WeakReference<AssemblyLoadContext> WeakAlc) RunPoisonousSerializeAndTriggerUnload(SynapseLoader loader, SynapseBuild build)
    {
        var loaded = loader.Load(build);
        var alc = AssemblyLoadContext.GetLoadContext(loaded.Assembly)!;
        var weakAlc = new WeakReference<AssemblyLoadContext>(alc);

        var stateType = loaded.States[0];
        var initialCustomState = Activator.CreateInstance(stateType, 99m)!;

        // Deliberately serialize using the global static RuntimeProtocol.Json instead of OptionsFor(stateType)
        _ = JsonSerializer.SerializeToElement(initialCustomState, stateType, RuntimeProtocol.Json);

        var unloadTask = loader.Unload(loaded, gcRounds: 3);
        return (unloadTask, weakAlc);
    }
}
