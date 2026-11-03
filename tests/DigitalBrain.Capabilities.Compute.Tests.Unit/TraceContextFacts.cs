using DigitalBrain.Capabilities.Compute;

namespace DigitalBrain.Capabilities.Compute.Tests.Unit;

public sealed class TraceContextFacts
{
    [Fact]
    public void EnterSetsCurrentAndDisposeRestores()
    {
        var outer = new Trace
        {
            TraceId = "outer-trace",
            PayerAccountId = "acct-1",
            BrainId = "brain-1",
            Origin = new NeuronId(new BrainId("brain-1"), "edge", "request"),
        };
        var inner = new Trace
        {
            TraceId = "inner-trace",
            PayerAccountId = "acct-2",
            BrainId = "brain-2",
            Origin = new NeuronId(new BrainId("brain-2"), "edge", "request"),
        };

        Assert.Null(TraceContext.Current);
        using (TraceContext.Enter(outer))
        {
            Assert.Same(outer, TraceContext.Current);
            using (TraceContext.Enter(inner))
            {
                Assert.Same(inner, TraceContext.Current);
            }
            Assert.Same(outer, TraceContext.Current);
        }
        Assert.Null(TraceContext.Current);
    }

    [Fact]
    public void BeginCreatesATraceWithTheGivenBudget()
    {
        var origin = new NeuronId(new BrainId("brain-1"), "edge", "request");
        var trace = TraceContext.Begin("acct-1", "brain-1", origin, budgetMicros: 42_000);

        Assert.False(string.IsNullOrWhiteSpace(trace.TraceId));
        Assert.Equal("acct-1", trace.PayerAccountId);
        Assert.Equal("brain-1", trace.BrainId);
        Assert.Equal(origin, trace.Origin);
        Assert.Equal(42_000, trace.BudgetMicros);
    }
}
