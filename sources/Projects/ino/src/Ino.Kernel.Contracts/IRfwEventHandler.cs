using Ino.Core;

namespace Ino.Kernel.Contracts;

/// <summary>
/// Plan-side hook for receiving RFW callback events. When a remote widget
/// in the Flutter client dispatches an <c>event 'name' { args }</c>, the
/// gateway resolves the correlation_id back to this grain and calls
/// <see cref="HandleRfwEventAsync"/>. The returned <see cref="NeuronResult"/>
/// flows into the user's open chat stream as the next frame — typically
/// carrying a fresh <see cref="RfwPayload"/> for the next plan step
/// (e.g. <c>flight.selected</c> &rarr; hotel cards).
/// </summary>
public interface IRfwEventHandler
{
    Task<NeuronResult> HandleRfwEventAsync(
        string eventName,
        IReadOnlyDictionary<string, string> args,
        CancellationToken ct);
}
