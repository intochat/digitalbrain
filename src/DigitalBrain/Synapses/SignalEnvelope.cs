using System.Text.Json;

namespace DigitalBrain;

/// <summary>
/// A signal as an alias reaction receives it: named by its journal alias, with its payload as JSON, so a synapse source can react to
/// a signal whose type lives in another unit's load context.
/// </summary>
/// <param name="Alias">The signal's <c>[Signal]</c> alias.</param>
/// <param name="Payload">The signal serialised with the options the journal persists signals with.</param>
/// <param name="TraceId">The operation the signal belongs to, or null.</param>
/// <param name="CausationId">The <see cref="Causation.Address"/> of the signal whose reaction published this one, or null.</param>
public sealed record SignalEnvelope(string Alias, JsonElement Payload, string? TraceId, string? CausationId);
