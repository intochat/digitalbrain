using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

internal static class Providers
{
    // A missing provider is a rejected signal, not a crash: the caller named something the
    // host never configured, and the message says exactly where to configure it.
    internal static IChatClient Resolve(IServiceProvider services, string? provider, string? model)
    {
        ArgumentNullException.ThrowIfNull(services);

        var name = !string.IsNullOrWhiteSpace(model) ? model
            : !string.IsNullOrWhiteSpace(provider) ? provider
            : services.GetService<AIDefaults>()?.DefaultModel;
        if (string.IsNullOrWhiteSpace(name))
        {
            return services.GetRequiredService<IChatClient>();
        }

        var client = ResolveClient(services, name);
        // An explicitly configured provider can accept model ids outside our marker catalogue.
        if (client is null && !string.IsNullOrWhiteSpace(provider))
        {
            client = ResolveClient(services, provider);
        }

        return client ?? throw new SignalRejectedException(
            $"Provider '{name}' is not configured. Name a catalogued model marker or id, "
            + "configure DigitalBrain:AI:{Provider}:ApiKey for its provider, "
            + "or omit provider and model to use the configured default. "
            + $"Known models: {string.Join(", ", LLMModel.All.Select(static m => m.Marker.Name))}.");
    }

    private static IChatClient? ResolveClient(IServiceProvider services, string name)
    {
        var model = LLMModel.FindByMarkerName(name) ?? LLMModel.FindById(name);
        return model is not null
            ? services.GetKeyedService<IChatClient>(model.Marker)
            : services.GetKeyedService<IChatClient>(name);
    }
}
