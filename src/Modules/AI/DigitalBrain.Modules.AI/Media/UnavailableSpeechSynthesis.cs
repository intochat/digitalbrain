using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI.Media;

internal sealed class UnavailableSpeechSynthesis : ISpeechSynthesisTransport
{
    public bool IsAvailable => false;
    public string Model => "";
    public Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException("Configure a speech synthesis transport.");
}
