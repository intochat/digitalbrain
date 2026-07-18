using System.Diagnostics;
using FluentAssertions;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public sealed class SynapseObserverTests
{
    static readonly ActivitySource _testSource = new("ino");

    [Fact]
    public void Captures_synapse_handle_activities_with_matching_correlation_id()
    {
        using var observer = new SynapseObserver(correlationId: "corr-1");

        using (var act = _testSource.StartActivity("ino.neuron.handle"))
        {
            act?.SetTag("ino.synapse.type", "PlanTripRequest");
            act?.SetTag("ino.correlation_id", "corr-1");
        }

        using (var act = _testSource.StartActivity("ino.neuron.handle"))
        {
            act?.SetTag("ino.synapse.type", "FindFlightsRequest");
            act?.SetTag("ino.correlation_id", "other-corr");
        }

        observer.Observed.Should().HaveCount(1);
        observer.Observed[0].Type.Should().Be("PlanTripRequest");
        observer.Observed[0].CorrelationId.Value.Should().Be("corr-1");
    }
}
