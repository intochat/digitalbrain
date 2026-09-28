using DigitalBrain.Identity.Configuration;
using DigitalBrain.Identity;
using Microsoft.Extensions.Options;

namespace IntoChat;

internal static class IntoChatConfiguration
{
    internal static IServiceCollection AddIntoChatOptions(this IServiceCollection services)
    {
        services.AddOptions<GraphOptions>().BindConfiguration(GraphOptions.SectionName);
        services.AddOptions<BasicAuthOptions>().BindConfiguration(BasicAuthOptions.SectionName);
        services.AddOptions<WorkspaceStorageOptions>().BindConfiguration(WorkspaceStorageOptions.SectionName);
        services.AddOptions<SessionStreamOptions>().BindConfiguration(SessionStreamOptions.SectionName)
            .Validate(options => options.PollInterval > TimeSpan.Zero, "Session stream PollInterval must be positive.")
            .ValidateOnStart();
        return services;
    }
}