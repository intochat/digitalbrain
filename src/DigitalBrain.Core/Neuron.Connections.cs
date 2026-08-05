namespace DigitalBrain;

public abstract partial class Neuron
{
    private Task ReceiveConnectAsync(Connect connect, DeliveryEnvelope envelope)
        => RunCoreReceptionAsync(connect, envelope, (heardFrom, _, now) =>
        {
            if (ConnectRefusalOf(connect) is { } reason)
            {
                return StageCoreSaid(
                    new ConnectionRefused(heardFrom.ToSynapseRef(), connect.Fact, connect.To, reason),
                    heardFrom,
                    now,
                    directedTo: envelope.Source);
            }

            journal.AddConnection(connect.Fact, connect.To);
            return false;
        });

    private Task ReceiveDisconnectAsync(Disconnect disconnect, DeliveryEnvelope envelope)
        => RunCoreReceptionAsync(disconnect, envelope, (_, _, _) =>
        {
            journal.RemoveConnection(disconnect.Fact, disconnect.To);
            return false;
        });

    private string? ConnectRefusalOf(Connect connect)
    {
        if (!catalog.TryGetFactType(connect.Fact, out var factType))
        {
            return $"'{connect.Fact}' is not a fact kind in the running catalog";
        }

        if (catalog.IsQuestion(factType))
        {
            return $"'{connect.Fact}' is a question; questions are not connectable — "
                + "a connected second answerer instance would mint duplicate replies";
        }

        if (!catalog.TryGetNeuronType(connect.To.Kind, out _))
        {
            return $"'{connect.To.Kind}' is not a neuron kind in the running catalog";
        }

        return catalog.ListensTo(connect.To.Kind, factType)
            ? null
            : $"'{connect.To.Kind}' does not declare INeuron<{factType.Name}>";
    }

    private async Task RunCoreReceptionAsync(
        Synapse fact,
        DeliveryEnvelope envelope,
        Func<SynapseRefEntry, long, DateTimeOffset, bool> stageOutcome)
    {
        bool deliverable;
        try
        {
            var now = clock.GetUtcNow();
            var heardFrom = SynapseRefEntry.From(new SynapseRef(envelope.Source, envelope.Sequence));
            var heardPosition = journal.AppendHeard(
                catalog.KindOfFact(fact.GetType()),
                envelope.Timestamp,
                heardFrom,
                envelope.Cause is { } cause ? SynapseRefEntry.From(cause) : null,
                envelope.Answers is { } answers ? SynapseRefEntry.From(answers) : null,
                codec.Encode(fact));
            deliverable = stageOutcome(heardFrom, heardPosition, now);
            journal.SetWatermark(envelope.Source, envelope.Sequence, now);
        }
        catch
        {
            Poison();
            throw;
        }

        await CommitCoreBatchAsync(deliverable);
        SyncScheduleTimers();
    }
}
