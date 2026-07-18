using Ino.Core;
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans.Runtime;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class InoInstanceContextFilterTests
{
    [Fact]
    public async Task Non_InoNeuron_grain_passes_through()
    {
        var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var grain = Substitute.For<IGrainContext>();
        grain.GrainReference.Returns((GrainReference)null!);
        ctx.TargetContext.Returns(grain);
        ctx.Grain.Returns(new object());

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    [Fact]
    public async Task InoNeuron_grain_with_matching_RequestContext_passes()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "alice");
            RequestContext.Set(InoRequestContextKeys.SessionId, "default");

            var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
            var ctx = StubInoNeuronCallContext("alice/default");

            await filter.Invoke(ctx);

            await ctx.Received(1).Invoke();
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task InoNeuron_grain_with_mismatching_RequestContext_throws()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "mallory");
            RequestContext.Set(InoRequestContextKeys.SessionId, "default");

            var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
            var ctx = StubInoNeuronCallContext("alice/default");

            var ex = await Assert.ThrowsAsync<InoInstanceMismatchException>(() => filter.Invoke(ctx));
            Assert.Equal("alice/default", ex.ExpectedKey);
            Assert.Equal("mallory", ex.ActualUserId);
            await ctx.DidNotReceive().Invoke();
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task InoNeuron_grain_with_null_RequestContext_passes_in_permissive_mode()
    {
        // No RequestContext keys set — gateway warm-up window. Filter logs
        // at Debug and lets the call through. Grain itself is responsible for
        // sourcing identity from its grain key when context is empty.
        var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
        var ctx = StubInoNeuronCallContext("alice/default");

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    private static IIncomingGrainCallContext StubInoNeuronCallContext(string primaryKey)
    {
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var grain = Substitute.For<IInoNeuron, IGrain>();
        ctx.Grain.Returns(grain);

        var grainContext = Substitute.For<IGrainContext>();
        var grainId = GrainId.Create(GrainType.Create("ino-neuron"), primaryKey);
        grainContext.GrainId.Returns(grainId);
        ctx.TargetContext.Returns(grainContext);
        return ctx;
    }
}
