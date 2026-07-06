using DigitalBrain.Core;

namespace DigitalBrain.Google;

// Marker interface so tests/callers can resolve the single GoogleAuthNeuron grain unambiguously —
// GetGrain<INeuron>(...) can't disambiguate among the 40+ concrete grain types in DigitalBrain.Kernel
// (same pattern as grain-specific contracts such as IGeneratedNeuron and IDemoNeuron).
[Alias("DigitalBrain.Google.IGoogleAuthNeuron")]
public interface IGoogleAuthNeuron : INeuron, IHandle<Signal>
{
    Task<GoogleOAuthCallbackResult> CompleteOAuthAsync(GoogleOAuthCallback callback);
}
