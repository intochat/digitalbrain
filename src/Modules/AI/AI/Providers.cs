using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

internal static class Providers
{
    // A missing provider is a rejected signal, not a crash: the caller named something the
    // host never configured, and the message says exactly where to configure it.
    internal static IChatClient Resolve(IServiceProvider services, string? provider, string? model)
        => Resolve(services, provider, model, out _);

    internal static IChatClient Resolve(IServiceProvider services, string? provider, string? model, out bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ownsClient = false;

        if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(model))
        {
            return services.GetRequiredService<IChatClient>();
        }
        try
        {
            var profiles = services.GetRequiredService<ModelProfiles>();
            // Earlier Instruct messages put a model marker in Provider. Preserve that shape,
            // while an actual provider name must select that provider instead of being ignored.
            var legacyMarker = string.IsNullOrWhiteSpace(model) && provider is not null
                && (LLMModel.FindByMarkerName(provider) ?? LLMModel.FindById(provider)) is not null;
            var selection = legacyMarker ? new AgentModelSelection(Model: provider)
                : new AgentModelSelection(Provider: provider, Model: model);
            var client = profiles.CreateClient(profiles.Resolve(selection));
            ownsClient = true;
            return client;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            throw new SignalRejectedException(error.Message);
        }
    }
}
