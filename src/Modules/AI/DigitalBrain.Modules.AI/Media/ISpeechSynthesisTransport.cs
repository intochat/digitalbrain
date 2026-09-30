using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI.Media;

/// <summary>Host-only transport; no SDK types cross the neuron boundary.</summary>
public interface ISpeechSynthesisTransport
{
    bool IsAvailable { get; }
    string Model { get; }
    Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken);
}
