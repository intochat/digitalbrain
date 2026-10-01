using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.AI.Media;

public static class MediaHosting
{
    public static IServiceCollection AddMediaNeurons(this IServiceCollection services)
    {
        services.TryAddSingleton<ISpeechSynthesisTransport, UnavailableSpeechSynthesis>();
        return services;
    }

    public static IServiceCollection AddOpenAISpeechSynthesis(this IServiceCollection services, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        services.Replace(ServiceDescriptor.Singleton<ISpeechSynthesisTransport>(sp =>
            new OpenAI.OpenAISpeechSynthesis(model, sp.GetRequiredService<IAiCredentials>())));
        return services;
    }
}
