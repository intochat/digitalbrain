namespace DigitalBrain;

public sealed class DispatchTests
{
    [Fact]
    public void DirectDispatchRetainsExactlyOneRelativeReceiver()
    {
        var receiver = new NeuronId("salesforce", "account/acme");

        var dispatch = Dispatch.Direct(receiver);

        Assert.True(dispatch.IsDirect);
        Assert.Equal(receiver, dispatch.Receiver);
    }

    [Fact]
    public void BroadcastDispatchHasNoReceiver()
    {
        var dispatch = Dispatch.Broadcast;

        Assert.False(dispatch.IsDirect);
        Assert.Null(dispatch.Receiver);
    }

    [Fact]
    public void DirectDispatchRejectsAnIncompleteRelativeReceiver()
    {
        Assert.Throws<ArgumentException>(() => Dispatch.Direct(new NeuronId("", "account/acme")));
    }
}
