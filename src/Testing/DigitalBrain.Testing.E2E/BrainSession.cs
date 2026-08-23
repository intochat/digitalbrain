using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Client;
using DigitalBrain.Testing;

namespace DigitalBrain.Testing.E2E;

public sealed class BrainSession : IAsyncDisposable
{
    internal BrainSession(IDigitalBrain brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        Brain = brain;
        Owner = brain.Owner;
    }

    public IDigitalBrain Brain { get; }

    public OwnerId Owner { get; }

    public Task<SynapseDelivery> WaitForJournalAsync(
        NeuronId subject,
        JournalKind kind,
        Func<SynapseDelivery, bool> match,
        TimeSpan? timeout = null)
        => JournalWait.ForAsync(Brain, subject, kind, match, timeout);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
