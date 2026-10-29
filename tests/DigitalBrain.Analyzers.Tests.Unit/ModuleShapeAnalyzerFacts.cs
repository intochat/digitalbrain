using DigitalBrain.Analyzers;

namespace DigitalBrain.Analyzers.Tests.Unit;

public sealed class ModuleShapeAnalyzerFacts
{
    [Fact]
    public Task Module_without_manifest_reports_DB0001() => Verify<ModuleShapeAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [Module("x")] public sealed class {|DB0001:M|} : Module { }
        """);

    [Fact]
    public Task Module_without_attribute_reports_DB0001() => Verify<ModuleShapeAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class {|DB0001:M|} : Module { public static ModuleManifest Manifest { get; } = new(); }
        """);

    [Fact]
    public Task Instance_or_wrongly_typed_manifest_reports_DB0001() => Verify<ModuleShapeAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [Module("a")] public sealed class {|DB0001:A|} : Module { public ModuleManifest Manifest { get; } = new(); }
        [Module("b")] public sealed class {|DB0001:B|} : Module { public static object Manifest { get; } = new(); }
        """);

    [Fact]
    public Task Correct_module_reports_nothing() => Verify<ModuleShapeAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public abstract class Base : Module { }
        [Module("x")] public sealed class M : Base { public static ModuleManifest Manifest { get; } = new(); }
        public sealed class NotAModule { }
        """);
}

public sealed class AppSchedulingAnalyzerFacts
{
    [Fact]
    public Task Scheduling_attributes_in_app_class_report_DB0002() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [{|DB0002:Reentrant|}] public sealed class A : App
        {
            [{|DB0002:ReadOnlyCall|}] public void R() { }
            [{|DB0002:OneWayCall|}] public void O() { }
            [{|DB0002:Interleaves|}] public void I() { }
            [{|DB0002:InterleavesWhen("P")|}] public void W() { }
            [{|DB0002:CallTimeout(10)|}] public void T() { }
        }
        """);

    [Fact]
    public Task Scheduling_attribute_in_marked_app_class_reports_DB0002() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("a")] public sealed class A { [{|DB0002:ReadOnlyCall|}] public void R() { } }
        """);

    [Fact]
    public Task Scheduling_attributes_on_neuron_report_nothing() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [Neuron("n"), Reentrant] public sealed class N : Neuron
        {
            [ReadOnlyCall] public void R() { }
            [CallTimeout(10)] public void T() { }
        }
        """);
}

public sealed class OrleansAttributeAnalyzerFacts
{
    [Theory]
    [InlineData("[Neuron(\"n\")]")]
    [InlineData("[Signal(\"s\")]")]
    [InlineData("[App(\"a\")]")]
    [InlineData("[Module(\"m\")]")]
    public Task Orleans_attribute_on_identity_type_reports_DB0003(string identity) => Verify<OrleansAttributeAnalyzer>.Diagnostics($$"""
        using DigitalBrain;
        {{identity}} [Orleans.GenerateSerializer] public sealed class {|DB0003:T|} { }
        """);

    [Fact]
    public Task Orleans_attribute_on_plain_type_reports_nothing() => Verify<OrleansAttributeAnalyzer>.Diagnostics("""
        [Orleans.GenerateSerializer] public sealed class Legacy { }
        """);
}
