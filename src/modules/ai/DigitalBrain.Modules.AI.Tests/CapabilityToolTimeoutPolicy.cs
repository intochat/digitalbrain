using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class CapabilityToolTimeoutPolicy
{
    [Fact(DisplayName =
        "a capability tool outwaits more than one outbox delivery attempt, so a slow capability is not reported as failed")]
    public void ToolWaitOutlastsMoreThanOneDeliveryAttempt()
    {
        Assert.True(
            SynapseCapabilityTool.ToolResponseWait >= DeliveryPolicy.DeliveryAttemptTimeout * 2,
            $"A tool wait of {SynapseCapabilityTool.ToolResponseWait} gives up inside one "
            + $"{DeliveryPolicy.DeliveryAttemptTimeout} delivery attempt, so the model would be told a "
            + "capability failed while the outbox is still delivering it.");

        Assert.True(
            SynapseCapabilityTool.ToolResponseWait < DeliveryPolicy.RetryHorizon,
            "A tool must give up long before the outbox abandons the request, or a turn hangs for the "
            + "whole retry horizon.");
    }

    [Fact(DisplayName =
        "the tool timeout tells the model the request may still complete and names the correlation to look it up by")]
    public void TimeoutMessageKeepsTheRequestFindable()
    {
        var correlation = CorrelationId.New();
        var target = new NeuronId("chat", new OwnerId("owner-a"), "main");

        var message = SynapseCapabilityTool.ResponseTimeoutMessage(
            target,
            typeof(CapabilityToolSelected),
            correlation,
            SynapseCapabilityTool.ToolResponseWait);

        Assert.Contains(correlation.ToString(), message, StringComparison.Ordinal);
        Assert.Contains("may still complete", message, StringComparison.Ordinal);
        Assert.Contains(target.ToString(), message, StringComparison.Ordinal);
        Assert.Contains(nameof(CapabilityToolSelected), message, StringComparison.Ordinal);
    }
}
