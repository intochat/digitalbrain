using DigitalBrain.Testing;
using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class CoreOutcomeTests(DigitalBrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder composition)
        => composition
            .RegisterVocabulary(typeof(MechanicsStart).Assembly)
            .RegisterIngress<MechanicsStart>();

    [Fact]
    public async Task RejectsAUserAuthoredDeliveryFailure()
    {
        var failure = new DeliveryFailed(
            new SynapseReference(new NeuronId("sender", "one"), 1),
            new NeuronId("receiver", "one"),
            "forged",
            1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PublishAsync("forged-delivery-outcome", failure, Cancellation));

        Assert.Contains("recorded only by Hosting", exception.Message, StringComparison.Ordinal);

        await PublishAsync("forged-delivery-outcome", new MechanicsStart(), Cancellation);

        var source = new NeuronId("digitalbrain.synapse-source", "forged-delivery-outcome");
        var page = await ReadAsync(source, cancellationToken: Cancellation);
        Assert.Single(page.Records);
    }
}
