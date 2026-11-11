using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Compute;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Compute;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class CallerContextFacts
{
    [Fact]
    public void StampThenTryGetReturnsTheCaller()
    {
        var expected = CapabilityCallers.Of("person-1", "acc-1", "brain-1", CallerKind.User, conversationId: "conv-1");

        using (RuntimeCallerContext.StampScope(expected))
        {
            Assert.True(RuntimeCallerContext.TryGet(out var actual));
            Assert.Equal(expected, actual);
            Assert.Equal(expected, RuntimeCallerContext.Require());
            Assert.True(RuntimeCallerContext.IsModelOriginated(actual!));
            Assert.True(RuntimeCallerContext.IsTrusted(actual!));

            Assert.NotNull(TraceContext.Current);
            Assert.Equal("acc-1", TraceContext.Current!.PayerAccountId);
            Assert.Equal("brain-1", TraceContext.Current.BrainId);
            Assert.Equal(TraceRequestContext.OriginOf("brain-1", "brain-1/edge"), TraceContext.Current.Origin);
        }

        Assert.False(RuntimeCallerContext.TryGet(out _));
        Assert.Null(TraceContext.Current);
    }

    [Fact]
    public void RequireWithoutStampThrowsUntrusted()
    {
        Assert.False(RuntimeCallerContext.TryGet(out _));
        Assert.Throws<UntrustedCallerException>(() => RuntimeCallerContext.Require());

        var untrusted = new CapabilityCaller("", "acc-1", "brain-1");
        Assert.False(RuntimeCallerContext.IsTrusted(untrusted));
        Assert.Throws<UntrustedCallerException>(() => RuntimeCallerContext.StampScope(untrusted));
    }
}
