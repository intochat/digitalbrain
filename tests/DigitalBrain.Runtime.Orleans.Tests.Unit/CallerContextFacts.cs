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

    [Fact]
    public async Task ObsoleteStamperForwards()
    {
#pragma warning disable CS0618
        Assert.Equal("db.cap.principal", CallerContextStamper.RequestContextKey);
        Assert.Equal("intochatos.trusted-module-call", CallerContextStamper.ModuleCallKey);
        Assert.Equal("intochatos.trace", TraceRequestContext.RequestContextKey);

        var caller = CapabilityCallers.Of("person-2", "acc-2", "brain-2", CallerKind.User);
        Assert.True(CallerContextStamper.IsTrusted(caller));
        Assert.False(CallerContextStamper.IsModelOriginated(caller));

        using (CallerContextStamper.StampScope(caller))
        {
            Assert.True(CallerContextStamper.TryGet(out var active));
            Assert.Equal(caller, active);
            Assert.Equal(caller, CallerContextStamper.Require());
        }

        Assert.False(CallerContextStamper.TryGet(out _));

        await Task.Run(() =>
        {
            var stamped = CallerContextStamper.Stamp(caller);
            Assert.Equal(caller, stamped);
            Assert.Equal(caller, CallerContextStamper.Require());
        }, TestContext.Current.CancellationToken);

        Assert.False(CallerContextStamper.TryGet(out _));
#pragma warning restore CS0618
    }
}
