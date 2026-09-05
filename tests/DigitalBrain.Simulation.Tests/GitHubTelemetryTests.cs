using System.Collections.Concurrent;
using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk.Webhooks;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubTelemetryTests
{
    [Fact]
    public async Task Delayed_dispatch_uses_persisted_webhook_context_without_baggage_or_payloads()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "DigitalBrain.GitHub",
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var scenario = await GitHubScenario.StartAsync();
        using var actor = VerifiedActor.Enter(scenario.Actor);
        var delivery = Guid.NewGuid().ToString();
        GitHubWebhookReceipt receipt;
        ActivityTraceId trace;
        using (var request = new Activity("aspnet.fixture.webhook").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            request.TraceStateString = "vendor=fixture";
            request.AddBaggage("private-payload", "must-not-be-retained");
            trace = request.TraceId;
            Assert.Equal(WebhookAcceptance.Accepted, await scenario.Handler.HandleAsync(scenario.Signed(delivery), TestContext.Current.CancellationToken));
            receipt = Assert.Single(await scenario.Inbox.ReadPendingAsync());
        }
        Assert.True(ActivityContext.TryParse(receipt.TraceParent, receipt.TraceState, out var context));
        Assert.Equal(trace, context.TraceId);
        Assert.Equal("vendor=fixture", receipt.TraceState);
        using (var unrelated = new Activity("unrelated.worker.tick").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            unrelated.AddBaggage("worker-secret", "must-not-be-propagated");
            Assert.NotEqual(trace, unrelated.TraceId);
            var dispatcher = new NeuronId("github-dispatcher", scenario.Binding.Owner, scenario.Binding.InstanceName);
            await scenario.Simulation.Grains.GetGrain<IGitHubRepositoryDispatcher>(dispatcher.ToGrainId())
                .DispatchAsync(scenario.Binding.Id, receipt, TestContext.Current.CancellationToken);
        }
        var dispatch = Assert.Single(stopped, activity => activity.OperationName == "github.webhook.dispatch"
            && Equals(activity.GetTagItem("github.delivery.id"), delivery));
        Assert.Equal(trace, dispatch.TraceId);
        Assert.Equal(context.SpanId, dispatch.ParentSpanId);
        Assert.Empty(dispatch.Baggage);
        Assert.Equal("fixture", dispatch.GetTagItem("github.binding.id"));
        Assert.Equal(42L, dispatch.GetTagItem("github.repository.id"));
        Assert.Equal(1, dispatch.GetTagItem("github.pull_request.number"));
        Assert.DoesNotContain(dispatch.TagObjects, tag => tag.Key.Contains("payload", StringComparison.OrdinalIgnoreCase)
            || tag.Key.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(stopped, activity => activity.OperationName == "github.webhook.persist"
            && activity.TraceId == trace && Equals(activity.GetTagItem("github.webhook.persisted"), true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-1234567890123456-01")]
    public void Invalid_persisted_parent_cannot_supply_a_trace_or_vendor_state(string? parent)
    {
        var receipt = GitHubTelemetry.SanitizeContext(new("delivery", new string('0', 64), "binding", null, false,
            DateTimeOffset.UtcNow, TraceParent: parent, TraceState: "vendor=fixture"));
        Assert.Null(receipt.TraceParent);
        Assert.Null(receipt.TraceState);
    }

    [Fact]
    public void Capture_rejects_hierarchical_context_and_overlong_vendor_state()
    {
        var receipt = new GitHubWebhookReceipt("delivery", new string('0', 64), "binding", null, false, DateTimeOffset.UtcNow);
        using (var hierarchical = new Activity("hierarchical").SetIdFormat(ActivityIdFormat.Hierarchical).Start())
        {
            Assert.Null(GitHubTelemetry.CaptureContext(receipt).TraceParent);
        }
        using var w3c = new Activity("w3c").SetIdFormat(ActivityIdFormat.W3C).Start();
        w3c.TraceStateString = new string('a', 513);
        var captured = GitHubTelemetry.CaptureContext(receipt);
        Assert.NotNull(captured.TraceParent);
        Assert.Null(captured.TraceState);
    }
}
