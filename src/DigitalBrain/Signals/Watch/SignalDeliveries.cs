using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalBrain.Signals.Watch;

// A filter over brain.On<T> (an app operation's synapse reading its app's AppRequested) marks the signals it
// passes over, so the runtime client acknowledges a batch nobody handled as skipped, not as a run.
// Runtime SPI: app filters call Skip and the runtime clients read WasSkipped. Hidden from
// script completion; it only marks local delivery bookkeeping.
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SignalDeliveries
{
    // A Signal or a plain [Signal] record (a plain record such as AppRequested).
    private static readonly ConditionalWeakTable<object, object> Skipped = [];

    public static void Skip(object signal) => Skipped.AddOrUpdate(signal, Skipped);

    public static bool WasSkipped(object signal) => Skipped.TryGetValue(signal, out _);
}
