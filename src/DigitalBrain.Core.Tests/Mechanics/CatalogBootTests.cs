using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class CatalogBootTests
{
    [Fact]
    public void UsesExplicitLogicalKindsForRegisteredBehaviors()
    {
        var catalog = new DigitalBrainComposition()
            .RegisterVocabulary(typeof(MechanicsPulse).Assembly)
            .RegisterNeuron<MechanicsEmitter>("mechanics.catalog.left")
            .RegisterNeuron<MechanicsReceiver>("mechanics.catalog.right")
            .Seal();

        Assert.True(catalog.HasNeuronKind("mechanics.catalog.left"));
        Assert.True(catalog.HasNeuronKind("mechanics.catalog.right"));
    }

    [Fact]
    public void UsesTheCSharpFullNameAsTheCanonicalSynapseKind()
    {
        var catalog = new DigitalBrainComposition()
            .RegisterVocabulary(typeof(MechanicsPulse).Assembly)
            .RegisterNeuron<MechanicsReceiver>("mechanics.catalog.left")
            .Seal();

        Assert.Equal(typeof(MechanicsPulse).FullName, catalog.KindOfSynapse(typeof(MechanicsPulse)));
    }

    [Fact]
    public void RejectsDuplicateLogicalKinds()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new DigitalBrainComposition()
            .RegisterVocabulary(typeof(MechanicsPulse).Assembly)
            .RegisterNeuron<MechanicsEmitter>("mechanics.catalog.same")
            .RegisterNeuron<MechanicsReceiver>("mechanics.catalog.same")
            .Seal());

        Assert.Contains("mechanics.catalog.same", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAHandlerWhoseSynapseWasNotRegisteredAsVocabulary()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new DigitalBrainComposition()
            .RegisterNeuron<MechanicsReceiver>("mechanics.catalog.left")
            .Seal());

        Assert.Contains("was not registered as vocabulary", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTheHostingOwnedDeliveryFailureAsExternalIngress()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new DigitalBrainComposition()
            .RegisterIngress<DeliveryFailed>()
            .Seal());

        Assert.Contains("Hosting-only", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIngressThatWasNotRegisteredAsVocabulary()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => new DigitalBrainComposition()
            .RegisterIngress<MechanicsPulse>()
            .Seal());

        Assert.Contains("not registered vocabulary", failure.Message, StringComparison.Ordinal);
    }
}
