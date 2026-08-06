using DigitalBrain.Testing.Mechanics;

namespace DigitalBrain;

public sealed class DeliveryFailurePolicyTests
{
    [Fact]
    public void StopsAtTheTerminalDeliveryOutcome()
    {
        Assert.False(DeliveryFailurePolicy.ShouldProduceFor(typeof(DeliveryFailed).FullName!));
        Assert.True(DeliveryFailurePolicy.ShouldProduceFor(typeof(RetryPulse).FullName!));
    }
}
