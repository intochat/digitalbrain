using System.Reflection;
using System.Reflection.Emit;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("other")]
public sealed record OtherState(int X = 0)
{
    public OtherState() : this(0) { }
}

[State("optionalonly")]
public sealed record OptionalOnly(int X = 0);

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
        Assert.Equal(3, c.For("week").Count);
    }

    [Fact]
    public void GatesOfReturnsMostDerivedSignalFirst()
    {
        var gates = SynapseCatalog.Scan(typeof(SynapseCatalogFacts).Assembly).GatesOf(typeof(Charging));
        Assert.Equal([nameof(Registered.Gate), nameof(Registered.GateOnBase)], gates.Select(g => g.Function.Name));
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
