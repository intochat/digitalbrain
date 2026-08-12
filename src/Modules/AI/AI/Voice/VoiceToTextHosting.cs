using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

internal static class VoiceToTextHosting
{
    public const string ModelIdConfigurationKey = "DigitalBrain:AI:Whisper:ModelId";

    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IAudioConverter, OggOpusToWavConverter>();

        // Foundry Local is registered by the Windows kernel host when ModelId is set.
        // Without it, STT refuses settled with a clear fix path.
        if (string.IsNullOrWhiteSpace(configuration[ModelIdConfigurationKey]))
        {
            services.TryAddSingleton<IAudioTranscriptionService, UnavailableTranscriptionService>();
        }
    }
}
