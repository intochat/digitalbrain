using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit;

// Default verifier only (never Verifiers.XUnit). The DigitalBrain and Orleans attributes are stubs declared in
// test source, so the tests reference neither package; the analyzers match by full metadata name.
internal static class Verify<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    internal const string Stubs = """
        namespace DigitalBrain
        {
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class NeuronAttribute(string type) : System.Attribute { public string Type { get; } = type; }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class SignalAttribute(string alias) : System.Attribute { public string Alias { get; } = alias; }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class AppAttribute(string id) : System.Attribute { public string Id { get; } = id; }
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class ModuleAttribute(string id) : System.Attribute { public string Id { get; } = id; }
            public sealed class InterleavesAttribute : System.Attribute;
            public sealed class InterleavesWhenAttribute(string predicate) : System.Attribute { public string Predicate { get; } = predicate; }
            public sealed class ReadOnlyCallAttribute : System.Attribute;
            public sealed class OneWayCallAttribute : System.Attribute;
            public sealed class CallTimeoutAttribute(int milliseconds) : System.Attribute { public int Milliseconds { get; } = milliseconds; }
            public sealed class ReentrantAttribute : System.Attribute;
            public abstract class Module { }
            public interface IScreen { }
            public abstract class Screen<TState> : Neuron, IScreen where TState : class, new()
            {
                protected abstract DigitalBrain.UI.View Render(TState state);
                protected System.Threading.Tasks.Task Show(TState next, params Signal[] facts) => System.Threading.Tasks.Task.CompletedTask;
                protected TState State { get; } = new();
            }
            public readonly record struct BrainId(string Value);
            public abstract class Neuron
            {
                protected T Get<T>(string key) where T : class => default!;
                protected System.Threading.Tasks.Task Check(string operation, params DigitalBrain.Rules.Rule[] rules) => System.Threading.Tasks.Task.CompletedTask;
            }
            public sealed class ModuleManifest { }
            public abstract record Signal;
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class StateAttribute(string alias) : System.Attribute { public string Alias { get; } = alias; }
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class SynapseAttribute : System.Attribute
            {
                public SynapseAttribute(System.Type from, string into) { Signal = from; State = into; }
                public SynapseAttribute(string from) { State = from; }
                public SynapseAttribute(string signalAlias, string into) { SignalAlias = signalAlias; State = into; }
                public string? SignalAlias { get; }
                public System.Type? Signal { get; }
                public string State { get; }
                public string Key { get; } = "default";
                public string? Every { get; init; }
            }
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class SlotAttribute : System.Attribute { public string? Name { get; init; } }
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class CapabilityAttribute(string name, int version) : System.Attribute { public string Name { get; } = name; public int Version { get; } = version; }
            public sealed class Reaction<T> { }
            public sealed record SignalEnvelope(string Alias, System.Text.Json.JsonElement Payload, string? TraceId, string? CausationId);
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class OnAttribute : System.Attribute;
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class ScenarioAttribute(string title) : System.Attribute;
        }
        namespace DigitalBrain.Time
        {
            public sealed record Tick(string Schedule, System.DateTimeOffset At) : DigitalBrain.Signal;
        }
        namespace DigitalBrain.Rules
        {
            public abstract record Rule(string Name) { public abstract bool Broken { get; } public abstract string Message { get; } }
            public sealed class Check { }
        }
        namespace DigitalBrain.Access
        {
            [System.AttributeUsage(System.AttributeTargets.Interface | System.AttributeTargets.Class)]
            public sealed class ShareableAttribute : System.Attribute;
        }
        namespace DigitalBrain.Capabilities
        {
            public readonly record struct CapabilityId(string Name, int Major)
            {
                public static CapabilityId Parse(string text) => default;
                public static bool TryParse(string? text, out CapabilityId id) { id = default; return false; }
            }
        }
        namespace DigitalBrain.UI
        {
            public sealed record View(string Title, object Root);
        }
        namespace Orleans
        {
            public sealed class GenerateSerializerAttribute : System.Attribute;
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface)]
            public sealed class AliasAttribute(string alias) : System.Attribute { public string Alias { get; } = alias; }
        }
        """;

    internal static Task Diagnostics(string source) => Create(source).RunAsync(TestContext.Current.CancellationToken);

    internal static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> Create(string source)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = source,
        };
        test.TestState.Sources.Add(("Stubs.cs", Stubs));
        return test;
    }
}
