using System.Diagnostics;

namespace DigitalBrain.Core;

internal static class SynapseTelemetry
{
    internal const string ReceiverTag = "db.receiver";

    internal const string SynapseTag = "db.synapse";

    internal const string CorrelationTag = "db.correlation";

    internal static readonly ActivitySource Source = new("DigitalBrain");

    internal static void WatcherDropped(DigitalBrain.Abstractions.Identity.NeuronId watched, Exception unreachable)
    {
        using var dropped = Source.StartActivity("db.watcher-dropped");

        dropped?.SetTag(ReceiverTag, watched.ToString());
        dropped?.SetTag("db.watcher-dropped", unreachable.GetType().Name);
    }

    internal static void BrainRegistrationDropped(DigitalBrain.Abstractions.Identity.NeuronId neuron, Exception unregistered)
    {
        using var dropped = Source.StartActivity("db.brain-registration-dropped");

        dropped?.SetTag(ReceiverTag, neuron.ToString());
        dropped?.SetTag("db.brain-registration-dropped", unregistered.GetType().Name);
    }

    internal static void ReplyDropped(
        DigitalBrain.Abstractions.Identity.NeuronId replier,
        DigitalBrain.Abstractions.Identity.NeuronId receiver,
        Exception undelivered)
    {
        using var dropped = Source.StartActivity("db.reply-dropped");

        dropped?.SetStatus(ActivityStatusCode.Error, undelivered.Message);
        dropped?.SetTag(ReceiverTag, receiver.ToString());
        dropped?.SetTag("db.replier", replier.ToString());
        dropped?.SetTag("db.reply-dropped", undelivered.GetType().Name);
    }
}
