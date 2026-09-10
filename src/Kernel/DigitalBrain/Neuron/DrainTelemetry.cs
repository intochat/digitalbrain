using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

// The drain swallows reaction failures to keep pending work; this is where they become visible.
internal static class DrainTelemetry
{
    private static readonly ActivitySource Source = new("DigitalBrain");

    internal static void BodyUnreadable(ILogger? logger, NeuronId neuron, string signalType, SignalId signal)
        => logger?.LogWarning(
            "Neuron {Neuron} signal {SignalType} {SignalId} body could not be read; the signal is journaled and ignored.",
            neuron,
            signalType,
            signal);

    internal static void Failed(ILogger? logger, NeuronId neuron, SignalId signal, Exception failure)
    {
        using var activity = Source.StartActivity("db.drain.failed");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("signal.id", signal.ToString());
        activity?.SetTag("exception.type", failure.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
        logger?.LogWarning(
            failure,
            "Neuron {Neuron} failed to react to signal {SignalId}; the head stays pending and the retry timer will rerun it.",
            neuron,
            signal);
    }

    internal static void AnnouncementsFailed(ILogger? logger, NeuronId neuron, Exception failure)
    {
        using var activity = Source.StartActivity("db.drain.announcements.failed");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("exception.type", failure.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
        logger?.LogWarning(
            failure,
            "Neuron {Neuron} failed to drain announcements; they stay stored and the retry timer will rerun them.",
            neuron);
    }

    internal static void Cancelled(ILogger? logger, NeuronId neuron, SignalId signal)
    {
        using var activity = Source.StartActivity("db.drain.cancelled");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("signal.id", signal.ToString());
        logger?.LogInformation(
            "Neuron {Neuron} reaction to signal {SignalId} stopped at its token; the entry is finished, not retried.",
            neuron,
            signal);
    }
}
