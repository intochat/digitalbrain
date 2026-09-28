using DigitalBrain.Apps.Assistant;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Applications;

internal static class ApplicationEndpoints
{
    public static IServiceCollection AddApplications(this IServiceCollection services) => services
        .AddSingleton(AppDefinition.Of<AssistantApp>())
        .AddSingleton<ApplicationCatalog>();

    public static void MapApplications(this IEndpointRouteBuilder routes)
    {
        var assistant = routes.MapGroup("/workspaces/{workspaceId}/applications/assistant")
            .AddEndpointFilter(WorkspaceAccessFilter.EnforceAsync);
        assistant.MapPost("/start", async (string workspaceId, ApplicationCatalog catalog, IOptions<BasicAuthOptions> auth) =>
        {
            await catalog.Start(AppDefinition.NameOf<AssistantApp>(), AppKey(auth.Value, workspaceId));
            return Results.NoContent();
        });
        assistant.MapGet("/chat", async (string workspaceId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
            Results.Ok(await Chat(brain, auth.Value, workspaceId).Read()));
        assistant.MapPost("/chat", async (string workspaceId, ChatMessage input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth) =>
        {
            var chat = Chat(brain, auth.Value, workspaceId);
            await chat.SetDraft(input.Text);
            await chat.Submit();
            return Results.Accepted();
        });
    }

    private static string AppKey(BasicAuthOptions auth, string workspaceId) => WorkspaceScope.Current(auth, workspaceId).Id + "/applications/assistant";

    private static IChat Chat(IDigitalBrain brain, BasicAuthOptions auth, string workspaceId) =>
        brain.Get<IChat>(UiComposer.NameOf(AppKey(auth, workspaceId), AssistantApp.ChatPart));

    internal sealed record ChatMessage(string Text);
}
