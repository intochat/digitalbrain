using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.BroadcastChannel;
using Orleans.Journaling;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
namespace DigitalBrain.Core;

internal sealed class SessionNeuron : Neuron, ISessionNeuron
{
    private const string ActivationPublishedName = "activation-published";

    private readonly IDurableValue<bool> _activationPublished;

    public SessionNeuron()
    {
        _activationPublished = ServiceProvider.GetRequiredKeyedService<IDurableValue<bool>>(ActivationPublishedName);
    }

    public async Task Activate()
    {
        if (_activationPublished.Value)
        {
            return;
        }

        // Journal first: DigitalBrainActivated in this session's OWN Outgoing journal is the
        // pinned activation footprint, whether or not any surface module subscribes.
        var activated = await StageOutgoingAsync(new DigitalBrainActivated(Id.Owner), cause: null)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var writer = ServiceProvider
            .GetRequiredService<IClusterClient>()
            .GetBroadcastChannelProvider(DigitalBrainNames.BroadcastChannelProvider)
            .GetChannelWriter<SignalDelivery>(ChannelId.Create(
                DigitalBrainNames.ActivationChannelNamespace,
                $"{Id.Owner.Value}/{DigitalBrainNames.ActivationSubscriberName}"));
        await writer.Publish(activated)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _activationPublished.Value = true;
        await WriteStateAsync().ConfigureAwait(true);
    }

    public Task<SignalDelivery> Fire(NeuronId receiver, Signal signal)
    {
        if (receiver.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"An owner '{Id.Owner}' session cannot fire at '{receiver}', which belongs to owner '{receiver.Owner}'.");
        }

        return SendAsync(receiver, signal);
    }

    public Task Emit(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        return base.EmitAsync(signal);
    }

    // The session is the reply sink for every client request: ReplyAsync addresses the caller,
    // and the caller of a client fire is always this cell. It declares no IHandle<T> for those
    // replies, so it must accept whatever arrives — being journaled IS the delivery's purpose.
    protected override Task OnUnboundSignalAsync(Signal signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence)
        => subject == Id
            ? ReadJournal(kind, afterSequence)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadJournal(kind, afterSequence);

    public Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer)
        => subject == Id
            ? Watch(kind, afterSequence, observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).Watch(kind, afterSequence, observer);

    public Task UnwatchNeuron(NeuronId subject, IJournalObserver observer)
        => subject == Id
            ? Unwatch(observer)
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).Unwatch(observer);
}
