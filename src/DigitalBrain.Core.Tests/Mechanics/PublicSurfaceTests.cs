using System.Reflection;

namespace DigitalBrain;

public sealed class PublicSurfaceTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceOrleans()
    {
        var references = typeof(Neuron).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            static reference => reference.Name?.Contains("Orleans", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NeuronIsAPlainProtectedBehaviorFacade()
    {
        var id = typeof(Neuron).GetProperty(
            "Id",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Equal(typeof(object), typeof(Neuron).BaseType);
        Assert.NotNull(id);
        Assert.True(id.GetMethod!.IsFamily);
        Assert.Empty(typeof(Neuron).GetInterfaces());
    }

    [Fact]
    public void PublicJournalSurfaceUsesRecordedSynapseLanguage()
    {
        var core = typeof(Neuron).Assembly;

        Assert.NotNull(core.GetType("DigitalBrain.JournalRecord"));
        Assert.NotNull(core.GetType("DigitalBrain.JournalPage"));
        Assert.NotNull(core.GetType("DigitalBrain.JournalHistoryUnavailable"));
        Assert.Null(core.GetType("DigitalBrain.JournalFact"));
        Assert.Null(core.GetType("DigitalBrain.NeuronReading"));
        Assert.Null(core.GetType("DigitalBrain.SynapseMetadata"));
        Assert.Null(core.GetType("DigitalBrain.SynapseRef"));
    }

    [Fact]
    public void AccessCapabilityAssemblyDependsOnCoreButNotOrleans()
    {
        var access = Assembly.Load("DigitalBrain.Access");
        var references = access.GetReferencedAssemblies();

        Assert.Contains(references, static reference => reference.Name == "DigitalBrain.Core");
        Assert.DoesNotContain(
            references,
            static reference => reference.Name?.Contains("Orleans", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void MechanicalModuleFixtureDoesNotReferenceOrleansOrAccess()
    {
        var module = Assembly.Load("DigitalBrain.Testing.Mechanics");
        var references = module.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            static reference => reference.Name?.Contains("Orleans", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(references, static reference => reference.Name == "DigitalBrain.Access");
    }
}
