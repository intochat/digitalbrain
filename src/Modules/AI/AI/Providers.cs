using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.AI;

internal static class Providers
{
    // An empty provider/model pair means the host's default chat client, which the
    // caller does not own. An explicit selection is resolved through the configured
    // profiles and presets, and its pipeline is owned by the caller.
    internal static IChatClient Resolve(IServiceProvider services, string? provider, string? model, out bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(provider) && string.IsNullOrWhiteSpace(model))
        {
            ownsClient = false;
            return services.GetRequiredService<IChatClient>();
        }

        var profiles = services.GetRequiredService<ModelProfiles>();
        ownsClient = true;
        return profiles.CreateClient(profiles.Resolve(new AgentModelSelection(Provider: provider, Model: model)));
    }
}