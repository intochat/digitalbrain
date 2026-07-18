using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;
using Orleans.Streams;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class BrainTraceFilterTests
{
    [Fact]
    public async Task Successful_call_emits_Ok_pulse_with_RequestContext_identity()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "alice");
            RequestContext.Set(InoRequestContextKeys.SessionId, "session-7");

            var (filter, sink) = MakeFilter();
            var ctx = StubCallContext("alice/session-7", "AskAsync");

            await filter.Invoke(ctx);

            var pulse = Assert.Single(sink.Emitted);
            Assert.Equal("alice", pulse.UserId);
            Assert.Equal("session-7", pulse.InoInstanceId);
            Assert.Equal("AskAsync", pulse.MethodName);
            Assert.Equal(BrainPulseStatus.Ok, pulse.Status);
            Assert.True(pulse.DurationMs >= 0);
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task Empty_RequestContext_falls_back_to_system_autonomic()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("anything", "Tick");

        await filter.Invoke(ctx);

        var pulse = Assert.Single(sink.Emitted);
        Assert.Equal("system", pulse.UserId);
        Assert.Equal(InoRequestContextKeys.AutonomicSessionId, pulse.InoInstanceId);
    }

    [Fact]
    public async Task Failed_call_emits_Failed_pulse_and_rethrows()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("alice/default", "BoomAsync");
        ctx.When(c => c.Invoke()).Do(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Invoke(ctx));

        var pulse = Assert.Single(sink.Emitted);
        Assert.Equal(BrainPulseStatus.Failed, pulse.Status);
        Assert.Equal("BoomAsync", pulse.MethodName);
    }

    [Fact]
    public async Task Invoke_serializes_first_argument_to_payload_json()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("alice/session-42", "HandleAsync", new { Greeting = "Hello", Target = "world" });

        await filter.Invoke(ctx);

        var pulse = Assert.Single(sink.Emitted);
        Assert.False(string.IsNullOrEmpty(pulse.PayloadJson),
            "PayloadJson must be populated when the grain call has arguments");
        Assert.Contains("Hello", pulse.PayloadJson);
    }

    [Fact]
    public async Task Invoke_emits_empty_payload_json_when_no_arguments()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("alice/session-0", "PingAsync");

        await filter.Invoke(ctx);

        var pulse = Assert.Single(sink.Emitted);
        Assert.Equal(string.Empty, pulse.PayloadJson);
    }

    [Fact]
    public async Task Sink_failure_does_not_block_grain_call()
    {
        // The brain stream is observability — if the sink throws (e.g.
        // memory provider not registered yet during silo warm-up), the
        // filter must swallow the sink exception and let the grain call
        // surface its real result.
        var (filter, sink) = MakeFilter();
        sink.ThrowOnEmit = new InvalidOperationException("sink down");
        var ctx = StubCallContext("alice/default", "Ping");

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    private static (BrainTraceFilter filter, RecordingPulseSink sink) MakeFilter()
    {
        var sink = new RecordingPulseSink();
        var filter = new BrainTraceFilter(sink, NullLogger<BrainTraceFilter>.Instance);
        return (filter, sink);
    }

    private static IIncomingGrainCallContext StubCallContext(
        string primaryKey, string methodName, object? firstArg = null)
    {
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var method = typeof(BrainTraceFilterTests).GetMethod(nameof(StubMethod),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        ctx.ImplementationMethod.Returns(method);
        ctx.InterfaceMethod.Returns(method);

        var grainContext = Substitute.For<IGrainContext>();
        var grainId = GrainId.Create(GrainType.Create("test"), primaryKey);
        grainContext.GrainId.Returns(grainId);
        ctx.TargetContext.Returns(grainContext);

        // Wire up the Orleans IInvokable Request so SerializePayload can read the first arg.
        var request = Substitute.For<IInvokable>();
        if (firstArg is not null)
        {
            request.GetArgumentCount().Returns(1);
            request.GetArgument(0).Returns(firstArg);
        }
        else
        {
            request.GetArgumentCount().Returns(0);
        }
        ctx.Request.Returns(request);

        // The filter reads context.ImplementationMethod.Name for MethodName.
        // For tests that need a custom method name, set the static override
        // BrainTraceFilter.MethodNameOverrideForTests.
        ctx.Grain.Returns(new object());
        BrainTraceFilter.MethodNameOverrideForTests = methodName;
        return ctx;
    }

    private static void StubMethod() { }

    private sealed class RecordingPulseSink : IBrainPulseSink
    {
        public List<BrainPulse> Emitted { get; } = new();
        public Exception? ThrowOnEmit { get; set; }

        public Task EmitAsync(BrainPulse pulse, CancellationToken ct)
        {
            if (ThrowOnEmit is not null) throw ThrowOnEmit;
            Emitted.Add(pulse);
            return Task.CompletedTask;
        }
    }
}
