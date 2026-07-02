using System.Linq;
using DigitalBrain.SourceGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Tests.SourceGen;

public class SynapseJsonContextGeneratorTests
{
    [Fact]
    public void Generator_Emits_JsonSerializable_For_Every_Synapse_Subtype_In_Compilation()
    {
        var compilation = CompileWithSynapseTypes("""
            public sealed record ProbeSynapse(string Text) : DigitalBrain.Core.Synapse(nameof(ProbeSynapse), System.DateTimeOffset.UtcNow);
            """);

        var result = RunGenerator(new SynapseJsonContextGenerator(), compilation);

        // The generator emits a JsonSerializerContext body directly (see SynapseJsonContextGenerator's
        // remarks for why [JsonSerializable]-attribute emission - what the plan originally sketched -
        // doesn't work: System.Text.Json's own bundled generator never sees another generator's output
        // within the same compile). So the discovered-type proof is the KnownSynapseTypes registration
        // and the typed accessor, not a [JsonSerializable] attribute.
        Assert.Contains(result.GeneratedSources, s => s.SourceText.ToString().Contains("typeof(ProbeSynapse)"));
        Assert.Contains(result.GeneratedSources, s => s.SourceText.ToString().Contains("JsonTypeInfo<ProbeSynapse> ProbeSynapse"));
    }

    [Fact]
    public void Generated_Context_Covers_Every_Concrete_Synapse_Subtype_In_DigitalBrain_Core()
    {
        var synapseSubtypes = typeof(DigitalBrain.Core.Synapse).Assembly.GetTypes()
            .Where(t => typeof(DigitalBrain.Core.Synapse).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var type in synapseSubtypes)
        {
            var typeInfo = DigitalBrain.Kernel.JournalJsonContext.Default.GetTypeInfo(type);
            Assert.NotNull(typeInfo); // throws/fails per-type if the generator missed one
        }
    }

    [Fact]
    public void Generated_Context_Covers_Every_Concrete_Synapse_Subtype_In_DigitalBrain_Developer()
    {
        // Coverage proof for a Synapse subtype living outside DigitalBrain.Core (GitCommitted/GitReverted)
        // - guards against a regression narrowing CollectCandidateTypes back down to just the Core assembly.
        var synapseSubtypes = typeof(DigitalBrain.Developer.GitCommitted).Assembly.GetTypes()
            .Where(t => typeof(DigitalBrain.Core.Synapse).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var type in synapseSubtypes)
        {
            var typeInfo = DigitalBrain.Kernel.JournalJsonContext.Default.GetTypeInfo(type);
            Assert.NotNull(typeInfo);
        }
    }

    // Reuses the same TPA + explicit-extra-assembly reference resolution the Foundry uses to compile
    // packs standalone at runtime, instead of duplicating reference-gathering logic here.
    private static Compilation CompileWithSynapseTypes(string source) =>
        DigitalBrain.Kernel.Foundry.FoundryCompilation.CreateWith(
            "SynapseJsonContextGeneratorTests_ProbeAssembly",
            source,
            typeof(DigitalBrain.Core.Synapse).Assembly);

    private static GeneratorRunResult RunGenerator(IIncrementalGenerator generator, Compilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Results.Single();
    }
}
