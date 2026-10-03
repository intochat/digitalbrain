using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests;

public sealed class ContractVocabularyFacts
{
    public interface IPublicNeuron : INeuron;
    [PlatformOnly] public interface IHiddenNeuron : INeuron;
    public sealed record PublicSignal : Signal;
    private sealed record HiddenSignal : Signal;
    public static class A { public interface ISame : INeuron; }
    public static class B { public interface ISame : INeuron; }

    [Fact]
    public void VocabularyContainsOnlyTheComposedPublicContracts()
    {
        var vocabulary = new ContractVocabulary([typeof(IPublicNeuron).Assembly]).Read();
        Assert.Contains(vocabulary, x => x.QualifiedName == typeof(IPublicNeuron).FullName && x.Kind == "neuron");
        Assert.DoesNotContain(vocabulary, x => x.QualifiedName == typeof(IHiddenNeuron).FullName);
        Assert.Empty(new ContractVocabulary([]).Read());
        Assert.Contains(vocabulary, x => x.QualifiedName == typeof(PublicSignal).FullName && x.Kind == "signal");
        Assert.DoesNotContain(vocabulary, x => x.QualifiedName == typeof(HiddenSignal).FullName);
        Assert.Equal(2, vocabulary.Count(x => x.Name == "ISame"));
    }
}
