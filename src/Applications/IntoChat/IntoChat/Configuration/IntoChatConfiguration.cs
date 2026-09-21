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

    internal static BasicAuthOptions ResolveAuthOptions(WebApplication app)
    {
        // Small standalone hosts can use the middleware without application setup.
        // Open-generic IOptions registration alone does not mean auth was configured.
        var configured = app.Services.GetServices<IConfigureOptions<BasicAuthOptions>>().Any()
            || app.Services.GetServices<IPostConfigureOptions<BasicAuthOptions>>().Any();
        return configured
            ? app.Services.GetRequiredService<IOptions<BasicAuthOptions>>().Value
            : app.Configuration.GetSection(BasicAuthOptions.SectionName).Get<BasicAuthOptions>() ?? new();
    }
}