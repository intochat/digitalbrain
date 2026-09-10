using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

// The drain swallows reaction failures to keep pending work; this is where they become visible.
internal static class DrainTelemetry
{
    private static readonly ActivitySource Source = new("DigitalBrain");

    internal static void Failed(ILogger? logger, NeuronId neuron, SignalId signal, Exception failure)
    {
        using var activity = Source.StartActivity("db.drain.failed");
        activity?.SetTag("neuron", neuron.ToString());
        activity?.SetTag("signal.id", signal.ToString());
        activity?.SetTag("exception.type", failure.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
        logger?.LogWarning(
            failure,
            "Neuron {Neuron} failed to react to signal {SignalId}; unless cancelled, the head stays pending and the retry timer will rerun it.",
            neuron,
            signal);
    }
}
