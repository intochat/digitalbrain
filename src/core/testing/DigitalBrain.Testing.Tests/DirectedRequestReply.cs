using DigitalBrain.Abstractions;
using DigitalBrain.TestingTests.Harness;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class DirectedRequestReply(TestingFixture fixture)
{
    [Fact(DisplayName = "ReplyAsync targets the request caller with the original correlation")]
    public async Task ReplyTargetsCallerWithOriginalCorrelation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEcho>(TestingScenario.Echo);

        var response = await test.Client.Get<IEcho>(echo.Id.Name)
            .SendAsync(new EchoRequest("ping"), cancellationToken);

        Assert.Equal("ping", response.Text);

        var request = await echo.Incoming.NextAsync<EchoRequest>(cancellationToken);
        var reply = await echo.Outgoing.NextAsync<EchoResponse>(cancellationToken);
        Assert.Equal(request.CorrelationId, reply.CorrelationId);
        Assert.Equal(echo.Id, reply.Caller);
        Assert.Equal(ISessionNeuron.ForOwner(test.Client.Owner), request.Caller);
    }

    [Fact(DisplayName = "request and response are journaled; replaying a handled request does not duplicate the reply")]
    public async Task ReplayDoesNotDuplicateProvenResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEcho>(TestingScenario.Echo);

        var first = await test.Client.Get<IEcho>(echo.Id.Name)
            .SendAsync(new EchoRequest("once"), cancellationToken);
        Assert.Equal("once", first.Text);

        var request = await echo.Incoming.NextAsync<EchoRequest>(cancellationToken);
        _ = await echo.Outgoing.NextAsync<EchoResponse>(cancellationToken);

        await test.Client.GetGrainProxy<IEcho>(echo.Id.Name).Redeliver(
            new SynapseDelivery(
                request.Synapse,
                request.SynapseId,
                request.CorrelationId,
                request.CausationId,
                request.Caller,
                request.Sequence,
                request.Timestamp));

        var outgoing = await echo.Outgoing.ReadAsync<EchoResponse>(cancellationToken: cancellationToken);
        Assert.Single(outgoing);
    }

    [Fact(DisplayName = "ReplyAsync outside an active delivery context is rejected")]
    public async Task ReplyOutsideDeliveryContextIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => test.Client.GetGrainProxy<IReplyProbe>(TestingScenario.ReplyProbe)
                .ReplyOutsideContext());

        Assert.Contains("active delivery context", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "directed reply remains owner-isolated")]
    public async Task DirectedReplyIsOwnerIsolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var ownerEcho = test.Neuron<IEcho>(TestingScenario.Echo);
        var other = test.Owner(TestingScenario.OtherOwner);
        var otherEcho = other.Neuron<IEcho>(TestingScenario.Echo);

        var mine = await test.Client.Get<IEcho>(ownerEcho.Id.Name)
            .SendAsync(new EchoRequest("owner"), cancellationToken);
        var theirs = await other.Client.Get<IEcho>(otherEcho.Id.Name)
            .SendAsync(new EchoRequest("guest"), cancellationToken);

        Assert.Equal("owner", mine.Text);
        Assert.Equal("guest", theirs.Text);

        var ownerIncoming = await ownerEcho.Incoming.ReadAsync<EchoRequest>(cancellationToken: cancellationToken);
        var otherIncoming = await otherEcho.Incoming.ReadAsync<EchoRequest>(cancellationToken: cancellationToken);
        Assert.All(ownerIncoming, entry => Assert.Equal(test.Client.Owner, entry.Caller.Owner));
        Assert.All(otherIncoming, entry => Assert.Equal(other.Id, entry.Caller.Owner));
    }

    [Fact(DisplayName = "concurrent typed requests receive only their own correlated responses")]
    public async Task ConcurrentRequestsReceiveOnlyOwnCorrelatedResponses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var left = test.Neuron<IEcho>("echo-left");
        var right = test.Neuron<IEcho>("echo-right");

        var firstTask = test.Client.Get<IEcho>(left.Id.Name)
            .SendAsync(new EchoRequest("first"), cancellationToken);
        var secondTask = test.Client.Get<IEcho>(right.Id.Name)
            .SendAsync(new EchoRequest("second"), cancellationToken);
        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal("first", (await firstTask).Text);
        Assert.Equal("second", (await secondTask).Text);

        var leftRequest = Assert.Single(await left.Incoming.ReadAsync<EchoRequest>(cancellationToken: cancellationToken));
        var rightRequest = Assert.Single(await right.Incoming.ReadAsync<EchoRequest>(cancellationToken: cancellationToken));
        var leftReply = Assert.Single(await left.Outgoing.ReadAsync<EchoResponse>(cancellationToken: cancellationToken));
        var rightReply = Assert.Single(await right.Outgoing.ReadAsync<EchoResponse>(cancellationToken: cancellationToken));
        Assert.Equal(leftRequest.CorrelationId, leftReply.CorrelationId);
        Assert.Equal(rightRequest.CorrelationId, rightReply.CorrelationId);
        Assert.NotEqual(leftRequest.CorrelationId, rightRequest.CorrelationId);
    }

    [Fact(DisplayName = "cancellation tears down the watch without corrupting a committed request delivery")]
    public async Task CancellationDoesNotCorruptCommittedDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEcho>("cancel-integrity");
        var reference = test.Client.Get<IEcho>(echo.Id.Name);

        var committed = await reference.SendAsync(new EchoRequest("committed"), cancellationToken);
        Assert.Equal("committed", committed.Text);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var cancelled = reference.SendAsync(new EchoRequest("cancelled-wait"), linked.Token);
        await linked.CancelAsync();

        try
        {
            await cancelled;
        }
        catch (OperationCanceledException)
        {
        }

        var requests = await echo.Incoming.ReadAsync<EchoRequest>(cancellationToken: cancellationToken);
        Assert.Contains(requests, entry => entry.Synapse.Text == "committed");

        var after = await reference.SendAsync(new EchoRequest("after-cancel"), cancellationToken);
        Assert.Equal("after-cancel", after.Text);
        var replies = await echo.Outgoing.ReadAsync<EchoResponse>(cancellationToken: cancellationToken);
        Assert.Contains(replies, entry => entry.Synapse.Text == "committed");
        Assert.Contains(replies, entry => entry.Synapse.Text == "after-cancel");
    }
}
