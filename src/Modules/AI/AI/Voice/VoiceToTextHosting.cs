using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI;

internal static class VoiceToTextHosting
{
    public const string DefaultTranscriptionKey = AIClients.DefaultTranscriptionKey;

    internal static void Add(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IAudioConverter, OggOpusToWavConverter>();

        var markerName = configuration[DefaultTranscriptionKey];
        if (string.IsNullOrWhiteSpace(markerName))
        {
            services.TryAddSingleton<IAudioTranscriptionService, UnavailableTranscriptionService>();
            return;
        }

        var model = TranscriptionModel.FindByMarkerName(markerName);
        if (model is null)
        {
            // An unknown marker must not take the kernel down over voice: the
            // endpoint reports it as 503 with this message, and every other
            // surface keeps working.
            services.TryAddSingleton<IAudioTranscriptionService>(
                new UnavailableTranscriptionService(
                    $"{DefaultTranscriptionKey} names unknown model '{markerName}'. "
                    + $"Known models: {string.Join(", ", TranscriptionModel.All.Select(static m => m.Marker.Name))}."));
            return;
        }

        // The provider decides the implementation — one key, no second flag.
        if (model.Provider is AiProvider.FoundryLocal)
        {
            services.TryAddSingleton<FoundryLocalTranscriptionService>();
            services.TryAddSingleton<IAudioTranscriptionService>(static sp =>
                sp.GetRequiredService<FoundryLocalTranscriptionService>());
            services.AddHostedService(static sp => sp.GetRequiredService<FoundryLocalTranscriptionService>());
            return;
        }

        services.TryAddSingleton<IAudioTranscriptionService>(
            new UnavailableTranscriptionService(
                $"{model.DisplayName} is served by {model.Provider}, which has no transcription "
                + "implementation yet."));
    }
}
