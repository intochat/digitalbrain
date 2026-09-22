using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI.Media;

/// <summary>Host-only transport; no SDK types cross the neuron boundary.</summary>
public interface ISpeechSynthesisTransport
{
    bool IsAvailable { get; }
    string Model { get; }
    Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken);
}

public static class MediaHosting
{
    public static IServiceCollection AddMediaNeurons(this IServiceCollection services)
    {
        services.TryAddSingleton<ISpeechSynthesisTransport, UnavailableSpeechSynthesis>();
        return services;
    }

    /// <summary>Enable OpenAI speech explicitly, using AIOptions.OpenAI credentials and endpoint.</summary>
    public static IServiceCollection AddOpenAISpeechSynthesis(this IServiceCollection services, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        services.Replace(ServiceDescriptor.Singleton<ISpeechSynthesisTransport>(sp =>
            new OpenAI.OpenAISpeechSynthesis(model, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AIOptions>>())));
        return services;
    }
}

internal sealed class UnavailableSpeechSynthesis : ISpeechSynthesisTransport
{
    public bool IsAvailable => false;
    public string Model => "";
    public Task<MediaPayload> Synthesize(SpeechSynthesisRequest request, CancellationToken cancellationToken)
        => throw new NotSupportedException("Configure a speech synthesis transport.");
}