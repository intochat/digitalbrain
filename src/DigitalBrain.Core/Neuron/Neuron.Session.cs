namespace DigitalBrain;

public abstract partial class Neuron
{
    [Alias("db.session")]
    internal interface ISessionEntry : IGrainWithStringKey
    {
        [Alias("emit")]
        Task EmitAsync(Synapse fact);

        [Alias("send")]
        Task SendAsync(NeuronId receiver, Synapse fact);

        [Alias("ask")]
        Task<SynapseRef> AskAsync(Synapse question);
    }

    internal sealed class Session : Neuron, ISessionEntry
    {
        private protected override bool ContinuesAsks => false;

        async Task ISessionEntry.EmitAsync(Synapse fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            RefusePoisoned();
            var staged = StagedFor(fact);

            bool deliverable;
            try
            {
                deliverable = StageSaid(
                    staged, cause: null, clock.GetUtcNow(), replyTo: null, journal.OpenAsksSnapshot());
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable);
        }

        async Task ISessionEntry.SendAsync(NeuronId receiver, Synapse fact)
        {
            ArgumentNullException.ThrowIfNull(fact);
            RefusePoisoned();
            var staged = StagedFor(fact);

            try
            {
                journal.AppendSaid(
                    staged.Kind,
                    clock.GetUtcNow(),
                    cause: null,
                    answers: null,
                    to: [NeuronIdEntry.From(receiver, NeuronIdEntry.Ask)],
                    staged.Body);
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable: true);
        }

        async Task<SynapseRef> ISessionEntry.AskAsync(Synapse question)
        {
            ArgumentNullException.ThrowIfNull(question);
            RefusePoisoned();
            var questionType = question.GetType();
            var staged = StagedFor(question);
            staged = catalog.TryGetAnswererKind(questionType, out var answererKind)
                ? staged with { AskAnswererKind = answererKind }
                : staged with { AskLacksAnswerer = true };

            long position;
            bool deliverable;
            try
            {
                position = journal.LastSeq + 1;
                deliverable = StageSaid(
                    staged, cause: null, clock.GetUtcNow(), replyTo: null, journal.OpenAsksSnapshot());
            }
            catch
            {
                Poison();
                throw;
            }

            await CommitCoreBatchAsync(deliverable);
            return new SynapseRef(Id, position);
        }
    }
}
