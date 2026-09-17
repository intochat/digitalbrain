using Microsoft.Extensions.Options;

namespace IntoChat;

internal static class ConversationalAgentEndpoints
{
    public static void AddConversationalAgent(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(services => new WorkspaceArtifactStore(
            services.GetRequiredService<IOptions<WorkspaceStorageOptions>>(),
            services.GetRequiredService<IHostEnvironment>()));
        builder.Services.AddSingleton<BehaviorConversationEndpoint>();
    }

    public static IEndpointConventionBuilder MapConversationalAgent(this IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/agent", (HttpContext context, BehaviorConversationEndpoint conversation)
            => conversation.RunAsync(context));
}
