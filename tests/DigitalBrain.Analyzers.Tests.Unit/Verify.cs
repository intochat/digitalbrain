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
            public abstract class App
            {
                protected AppKey Key(BrainId brain) => default;
                protected RequestLedger Requests { get; } = new RequestLedger();
                protected bool Present<TView>(System.Action<TView, TView> publish, System.Func<TView> side, System.Func<TView> main, string operation = "") where TView : class => true;
                protected void RecordError(System.Exception error, string operation = "") { }
                protected void Swallow(System.Exception error, string reason, string operation = "") { }
            }
            public readonly record struct BrainId(string Value);
            public readonly record struct AppKey
            {
                public static AppKey Parse(string path) => default;
                public static AppKey operator /(AppKey key, string part) => key;
                public static AppKey operator /(AppKey key, long part) => key;
                public static implicit operator string(AppKey key) => "";
            }
            public sealed class RequestLedger { public bool Handled(System.Guid id) => false; public bool Record(System.Guid id) => true; }
            public abstract class Neuron { }
            public sealed class ModuleManifest { }
            public abstract record Signal;
            [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class OnAttribute : System.Attribute;
        }
        namespace DigitalBrain.Capabilities
        {
            public readonly record struct CapabilityId(string Name, int Major)
            {
                public static CapabilityId Parse(string text) => default;
                public static bool TryParse(string? text, out CapabilityId id) { id = default; return false; }
            }
        }
        namespace Orleans
        {
            public sealed class GenerateSerializerAttribute : System.Attribute;
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
