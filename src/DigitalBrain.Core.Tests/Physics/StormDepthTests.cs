namespace DigitalBrain.Core.Tests.Physics;

public sealed class StormDepthPolicyTests
{
    [Fact(DisplayName = "L-R4 DeliveryPolicy MaximumDepth is 16")]
    public void MaximumDepthIsSixteen()
        => Assert.Equal(16, DeliveryPolicy.MaximumDepth);

    [Fact(DisplayName = "L-R4 Emission depth is reception depth plus one and exceeds at 17")]
    public void EmissionDepthArithmeticAndExceedsBound()
    {
        Assert.Equal(2, new DeliveryEnvelope(
            new NeuronId("a", "b"), 1, DateTimeOffset.UnixEpoch, null, null, Depth: 1).EmissionDepth);
        Assert.Equal(17, new DeliveryEnvelope(
            new NeuronId("a", "b"), 1, DateTimeOffset.UnixEpoch, null, null, Depth: 16).EmissionDepth);
        Assert.True(17 > DeliveryPolicy.MaximumDepth);
        Assert.False(16 > DeliveryPolicy.MaximumDepth);
    }
}
