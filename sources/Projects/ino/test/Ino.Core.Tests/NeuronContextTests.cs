using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Testing;
using Xunit;

namespace Ino.Core.Tests;

public class NeuronContextTests
{
    [Fact]
    public void Context_is_a_sealed_record()
    {
        Assert.True(typeof(NeuronContext).IsSealed);
    }

    [Fact]
    public void Context_supports_with_expression()
    {
        var ctx = NeuronContextForTest.Create(
            source: new Caller.Ambient(DomainId.From("kernel")));

        var forked = ctx with { CurrentEventId = EventId.New() };

        Assert.NotNull(forked.CurrentEventId);
        Assert.Null(ctx.CurrentEventId);
    }

    [Fact]
    public async Task Fire_forwards_to_FirePort_passing_this_as_caller()
    {
        var port = new CapturingFirePort();
        var ctx = NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("x")),
            firePort: port);

        await ctx.Fire(new DummySynapse(), TestContext.Current.CancellationToken);

        Assert.Same(ctx, port.LastCaller);
    }

    [Fact]
    public async Task FireBroadcast_forwards_to_FirePort_passing_this_as_caller()
    {
        var port = new CapturingFirePort();
        var ctx = NeuronContextForTest.Create(
            source: new Caller.Ambient(DomainId.From("domains")),
            firePort: port);

        await ctx.FireBroadcast(new DummySynapse(), TestContext.Current.CancellationToken);

        Assert.Same(ctx, port.LastCaller);
        Assert.Equal(CapturingFirePort.Mode.Broadcast, port.LastMode);
    }

    private sealed record DummySynapse : ISynapse;

    private sealed class CapturingFirePort : IFirePort
    {
        public enum Mode { Fire, Broadcast }

        public NeuronContext? LastCaller { get; private set; }
        public Mode LastMode { get; private set; }

        public Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        {
            LastCaller = caller; LastMode = Mode.Fire;
            return Task.FromResult(NeuronResult.Ok());
        }

        public Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
        {
            LastCaller = caller; LastMode = Mode.Broadcast;
            return Task.CompletedTask;
        }
    }
}
