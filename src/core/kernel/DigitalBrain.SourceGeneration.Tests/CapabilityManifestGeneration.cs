using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DigitalBrain.SourceGeneration.Tests;

public sealed class CapabilityManifestGeneration
{
    [Fact(DisplayName = "generator emits a capability manifest with stable ordered neuron and synapse IDs")]
    public void EmitsDeterministicCapabilityManifest()
    {
        var result = Run(
            """
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using Orleans;

            namespace Sample;

            public sealed partial class SampleModule : IModule;

            [Alias("sample.neuron")]
            [Description("Marker neuron")]
            public partial interface ISampleNeuron : INeuron;

            [Alias("sample.request")]
            [Description("Sample request")]
            public sealed record SampleRequest : RequestSynapse<SampleResponse>;

            [Alias("sample.response")]
            [Description("Sample response")]
            public sealed record SampleResponse : Synapse;

            [Alias("sample.other")]
            [Description("Other fact")]
            public sealed record OtherFact : Synapse;

            public sealed class SampleNeuron :
                ISampleNeuron,
                IHandle<SampleRequest>,
                IEmit<SampleResponse>,
                IEmit<OtherFact>
            {
                public Task HandleAsync(SampleRequest synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """);

        Assert.Empty(result.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Id.StartsWith("DBGEN", StringComparison.Ordinal)));

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("CapabilityManifest", generated, StringComparison.Ordinal);
        Assert.Contains("sample.neuron", generated, StringComparison.Ordinal);
        Assert.Contains("sample.other", generated, StringComparison.Ordinal);
        Assert.Contains("sample.request", generated, StringComparison.Ordinal);
        Assert.Contains("sample.response", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Sample.ISampleNeuron", generated, StringComparison.Ordinal);

        var otherIndex = generated.IndexOf("sample.other", StringComparison.Ordinal);
        var requestIndex = generated.IndexOf("sample.request", StringComparison.Ordinal);
        var responseIndex = generated.IndexOf("sample.response", StringComparison.Ordinal);
        Assert.True(otherIndex >= 0 && requestIndex >= 0 && responseIndex >= 0);
        Assert.True(requestIndex < otherIndex);
        Assert.True(otherIndex < responseIndex);
        Assert.Contains("Marker neuron", generated, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "generator scopes capabilities to the owning module namespace, not the whole assembly")]
    public void ScopesCapabilitiesToOwningModule()
    {
        var result = Run(
            """
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using Orleans;

            namespace Host.Alpha;

            public sealed partial class AlphaModule : IModule;

            [Alias("alpha.neuron")]
            [Description("Alpha neuron")]
            public partial interface IAlphaNeuron : INeuron;

            [Alias("alpha.ping")]
            [Description("Alpha ping")]
            public sealed record AlphaPing : Synapse;

            public sealed class AlphaNeuron : IAlphaNeuron, IHandle<AlphaPing>
            {
                public Task HandleAsync(AlphaPing synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }

            namespace Host.Beta;

            public sealed partial class BetaModule : IModule;

            [Alias("beta.neuron")]
            [Description("Beta neuron")]
            public partial interface IBetaNeuron : INeuron;

            [Alias("beta.ping")]
            [Description("Beta ping")]
            public sealed record BetaPing : Synapse;

            public sealed class BetaNeuron : IBetaNeuron, IHandle<BetaPing>
            {
                public Task HandleAsync(BetaPing synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """);

        Assert.Empty(result.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Id.StartsWith("DBGEN", StringComparison.Ordinal)));

        var alpha = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(source => source.Contains("partial class AlphaModule", StringComparison.Ordinal));
        var beta = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(source => source.Contains("partial class BetaModule", StringComparison.Ordinal));

        Assert.Contains("alpha.neuron", alpha, StringComparison.Ordinal);
        Assert.Contains("alpha.ping", alpha, StringComparison.Ordinal);
        Assert.DoesNotContain("beta.neuron", alpha, StringComparison.Ordinal);
        Assert.DoesNotContain("beta.ping", alpha, StringComparison.Ordinal);

        Assert.Contains("beta.neuron", beta, StringComparison.Ordinal);
        Assert.Contains("beta.ping", beta, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha.neuron", beta, StringComparison.Ordinal);
        Assert.DoesNotContain("alpha.ping", beta, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "generator reports DBGEN007 when a handled synapse is missing an Alias")]
    public void ReportsMissingAliasDiagnostic()
    {
        var result = Run(
            """
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using Orleans;

            namespace Sample;

            public sealed partial class SampleModule : IModule;

            [Alias("sample.neuron")]
            [Description("Marker neuron")]
            public partial interface ISampleNeuron : INeuron;

            [Description("Undescribed request")]
            public sealed record UndescribedRequest : Synapse;

            public sealed class SampleNeuron :
                ISampleNeuron,
                IHandle<UndescribedRequest>
            {
                public Task HandleAsync(UndescribedRequest synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBGEN007");
    }

    [Fact(DisplayName = "generator reports DBGEN009 when a capability neuron is missing a stable Alias")]
    public void ReportsMissingNeuronAliasDiagnostic()
    {
        var result = Run(
            """
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using Orleans;

            namespace Sample;

            public sealed partial class SampleModule : IModule;

            [Description("Marker neuron")]
            public partial interface ISampleNeuron : INeuron;

            [Alias("sample.request")]
            [Description("Sample request")]
            public sealed record SampleRequest : Synapse;

            public sealed class SampleNeuron :
                ISampleNeuron,
                IHandle<SampleRequest>
            {
                public Task HandleAsync(SampleRequest synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBGEN009");
    }

    [Fact(DisplayName = "generator reports DBGEN008 when a capability description is missing")]
    public void ReportsMissingDescriptionDiagnostic()
    {
        var result = Run(
            """
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using Orleans;

            namespace Sample;

            public sealed partial class SampleModule : IModule;

            [Alias("sample.neuron")]
            public partial interface ISampleNeuron : INeuron;

            [Alias("sample.request")]
            public sealed record SampleRequest : Synapse;

            public sealed class SampleNeuron :
                ISampleNeuron,
                IHandle<SampleRequest>
            {
                public Task HandleAsync(SampleRequest synapse, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBGEN008");
    }

    [Fact(DisplayName = "capability description diagnostic descriptor is registered for exact-catalog enforcement")]
    public void DescriptionDiagnosticDescriptorIsRegistered()
    {
        var field = typeof(DispatchManifestGenerator)
            .GetField("CapabilityDescriptionRequired", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CapabilityDescriptionRequired is missing.");
        var descriptor = (DiagnosticDescriptor)field.GetValue(null)!;
        Assert.Equal("DBGEN008", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
    }

    private static GeneratorDriverRunResult Run(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new DispatchManifestGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(Synapse).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DigitalBrain.Kernel.ICompiledModule).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DescriptionAttribute).Assembly.Location),
            ]);

        return CSharpCompilation.Create(
            "CapabilityManifestGenerationTests",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
