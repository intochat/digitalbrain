using System.Threading.Tasks;
using DigitalBrain.Analyzers.Neurons;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Neurons;

public sealed class ReentrancyJustificationAnalyzerFacts
{
    [Fact]
    public Task Reentrant_with_justification_comment_reports_nothing() => Verify<ReentrancyJustificationAnalyzer>.Diagnostics("""
        using DigitalBrain;
        // Justification: idempotent read cache allows concurrent interleaving safely.
        [Reentrant]
        public sealed class SafeCacheNeuron : Neuron { }
        """);

    [Fact]
    public Task Reentrant_without_justification_comment_reports_DB0032() => Verify<ReentrancyJustificationAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [Reentrant]
        public sealed class {|DB0032:UnsafeNeuron|} : Neuron { }
        """);

    [Fact]
    public Task Reentrant_without_comment_in_test_project_is_excluded()
    {
        var test = Verify<ReentrancyJustificationAnalyzer>.Create("""
            using DigitalBrain;
            [Reentrant]
            public sealed class MockNeuron : Neuron { }
            """);
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "is_global = true\nbuild_property.IsTestProject = true\n"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
