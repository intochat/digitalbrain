using System.Reflection;
using System.Reflection.Emit;
using DigitalBrain.Time;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("other")]
public sealed record OtherState(int X = 0)
{
    public OtherState() : this(0) { }
}

[State("optionalonly")]
public sealed record OptionalOnly(int X = 0);

[State("stats")]
public sealed record StatsState(int Runs = 0)
{
    public StatsState() : this(0) { }
}

public static class MorningStats
{
    [Synapse(typeof(Tick), "stats", Every = "07:00")]
    public static Task<StatsState> Refresh(Tick t, StatsState s, [Slot] ISqlRead Db) => Task.FromResult(s with { Runs = s.Runs + 1 });

    [Synapse("stats")]
    public static string Show(StatsState s) => s.Runs.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class SynapseCatalogFacts
{
    // A second record with a taken alias would poison the assembly-wide scans, so it lives in a throwaway dynamic assembly.
    private static Type Dynamic(string alias)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Dyn" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run).DefineDynamicModule("m");
        var type = module.DefineType("DynState", TypeAttributes.Public | TypeAttributes.Sealed);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(StateAttribute).GetConstructor([typeof(string)])!, [alias]));
        return type.CreateType();
    }

    [Fact]
    public void ScanFindsStatesAndSynapsesOfThisAssembly()
    {
        var c = SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly);
        Assert.Equal(typeof(WeekSpend), c.States["week"]);
        Assert.Contains(c.Synapses, d => d.Name.EndsWith(".Gate", StringComparison.Ordinal));
        Assert.Equal(3, c.For("week").Count(d => d.Function.DeclaringType == typeof(Registered)));
    }

    [Fact]
    public void GatesOfReturnsMostDerivedSignalFirst()
    {
        var gates = SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly).GatesOf(typeof(Charging));
        Assert.Equal([nameof(Registered.Gate), nameof(Registered.GateOnBase)], gates.Where(g => g.Function.DeclaringType == typeof(Registered)).Select(g => g.Function.Name));
    }

    [Fact]
    public void GatesOfIgnoresGatesOnOtherSignals()
    {
        var gates = SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly).GatesOf(typeof(SynapseStarted));
        Assert.Equal([nameof(Registered.GateOnBase)], gates.Select(g => g.Function.Name));
    }

    [Fact]
    public void AStateWithoutParameterlessConstructorIsRefusedByName()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseCatalog.Scan([typeof(NoDefault)]));
        Assert.Contains("NoDefault", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStateWithOnlyOptionalConstructorParametersIsRefusedByName()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseCatalog.Scan([typeof(OptionalOnly)]));
        Assert.Contains("OptionalOnly", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsOfDerivesTheInstallSentence()
    {
        var needs = SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly).NeedsOf("stats");
        Assert.Equal("stats wants to: read-only SQL through Db; run daily at 07:00 UTC; react to 1 signal; show a view.", needs.Sentence());
        Assert.Single(needs.Slots);
        Assert.Equal(["Refresh"], needs.Reactions);
    }

    [Fact]
    public void NeedsOfListsGatesBySignalAliasOrTypeName()
    {
        var needs = SynapseCatalog.Scan([typeof(WeekSpend), typeof(Registered)]).NeedsOf("week");
        Assert.Equal(["Signal", "test.charging"], needs.Gates.Order(StringComparer.Ordinal));
        Assert.True(needs.HasView);
        Assert.Empty(needs.Reactions);
    }

    [Fact]
    public void NeedsOfAnUnknownAliasThrows() =>
        Assert.Throws<SynapseShapeException>(() => SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly).NeedsOf("nobody"));

    [Fact]
    public void ASlotNameBoundToTwoContractsIsRefusedAtScan()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseCatalog.Scan([typeof(WeekSpend), ConflictingSlots()]));
        Assert.Contains("slot 'Db' is bound to two contracts", e.Message, StringComparison.Ordinal);
    }

    // Two reactions binding the slot name 'Db' to different contracts would poison the assembly-wide scans, so they are emitted.
    private static Type ConflictingSlots()
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Dyn" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run).DefineDynamicModule("m");
        var type = module.DefineType("Conflicting", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (var (method, contract) in new[] { ("A", typeof(ISqlRead)), ("B", typeof(IOtherContract)) })
        {
            var m = type.DefineMethod(method, MethodAttributes.Public | MethodAttributes.Static, typeof(WeekSpend), [typeof(Charging), typeof(WeekSpend), contract]);
            m.SetCustomAttribute(new CustomAttributeBuilder(typeof(SynapseAttribute).GetConstructor([typeof(Type), typeof(string)])!, [typeof(Charging), "week"]));
            m.DefineParameter(3, ParameterAttributes.None, "Db").SetCustomAttribute(new CustomAttributeBuilder(typeof(SlotAttribute).GetConstructor(Type.EmptyTypes)!, []));
            var il = m.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        return type.CreateType();
    }

    [Fact]
    public void ADuplicateAliasIsRefused()
    {
        var e = Assert.Throws<SynapseShapeException>(() => SynapseCatalog.Scan([typeof(WeekSpend), Dynamic("week")]));
        Assert.Contains("'week'", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MergeUnitesCatalogsAndRefusesDuplicateAliases()
    {
        var a = SynapseCatalog.Scan([typeof(WeekSpend), typeof(Registered)]);
        Assert.Throws<SynapseShapeException>(() => a.Merge(SynapseCatalog.Scan([Dynamic("week")])));
        var merged = a.Merge(SynapseCatalog.Scan([typeof(OtherState)]));
        Assert.Equal(2, merged.States.Count);
        Assert.Equal(3, merged.Synapses.Count);
    }
}

[Capability("other", 1)]
public interface IOtherContract;
