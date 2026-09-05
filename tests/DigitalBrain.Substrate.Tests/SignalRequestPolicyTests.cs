using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class SignalRequestPolicyTests
{
    private static readonly NeuronId Source = new("requestsource", new OwnerId("owner"), "policy");
    private static readonly NeuronId Target = new("requesttarget", new OwnerId("owner"), "policy");

    [Fact]
    public void ReplyIdentity_RequiresExactCausationSenderAndResponseType()
    {
        var request = Delivery(new ProbeRequest("actual", "normal"), Source);
        var sibling = Delivery(new ProbeRequest("sibling", "normal"), Source, request);
        var wrongCausation = Delivery(new ProbeResponse("wrong causation"), Target, sibling);
        var wrongSender = Delivery(new ProbeResponse("wrong sender"), Source, request);
        var wrongType = Delivery(new ProbeNoise(), Target, request);
        var correct = Delivery(new ProbeResponse("correct"), Target, request);
        Assert.Equal(request.CorrelationId, wrongCausation.CorrelationId);
        var mismatches = new JournalRead(3, [wrongCausation, wrongSender, wrongType], null);

        Assert.Null(SignalRequestPolicy.FindResponse(mismatches, Target, request, typeof(ProbeResponse)));
        Assert.Same(correct.Signal, SignalRequestPolicy.FindResponse(
            new(4, [wrongCausation, wrongSender, wrongType, correct], null),
            Target, request, typeof(ProbeResponse)));
    }

    [Fact]
    public async Task ResetRecovery_ReadsTheRetainedWindowOnce()
    {
        var reset = new JournalRead(700, [], new(700, 700, 189, 512, []));
        var retained = new JournalRead(700, [Delivery(new ProbeResponse("retained"), Target)], null);
        var reads = new List<long>();

        var result = await SignalRequestPolicy.RecoverRetainedAsync(reset, after =>
        {
            reads.Add(after);
            return Task.FromResult(retained);
        });

        Assert.Same(retained, result);
        Assert.Equal([188L], reads);
    }

    [Fact]
    public async Task RepeatedReset_FailsWithoutRetryingIndefinitely()
    {
        var reset = new JournalRead(700, [], new(700, 700, 189, 512, []));
        var reads = 0;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SignalRequestPolicy.RecoverRetainedAsync(reset, _ =>
            {
                reads++;
                return Task.FromResult(new JournalRead(900, [], new(900, 900, 389, 512, [])));
            }));

        Assert.Equal(1, reads);
        Assert.Contains("compacted again", failure.Message, StringComparison.Ordinal);
    }

    private static SignalDelivery Delivery(Signal signal, NeuronId caller, SignalDelivery? cause = null)
        => SignalDelivery.Create(signal, caller, 1, TimeProvider.System, cause);
}
