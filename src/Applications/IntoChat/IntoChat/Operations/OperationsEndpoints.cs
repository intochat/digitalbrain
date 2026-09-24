using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Operations;

internal static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/workspaces/{workspaceId}/reports",
            async (string workspaceId, ProblemReportInput input, IProblemReportStore reports,
                IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
            {
                if (!ValidIntentId(input.IntentId) || string.IsNullOrWhiteSpace(input.Message))
                {
                    return Results.BadRequest(new { error = "A report needs the intent id it came from and a message." });
                }
                var intentId = input.IntentId!;
                var message = input.Message!;

                WorkspaceScope scope;
                try { scope = ScopeOf(auth.Value, workspaceId); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }

                var report = await reports.AddAsync(scope.WorkspaceId, intentId, message.Trim(), ct);
                return Results.Created($"/workspaces/{workspaceId}/reports", report);
            });

        routes.MapDelete("/workspaces/{workspaceId}",
            async (string workspaceId, WorkspaceDeletion deletion, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
            {
                WorkspaceScope scope;
                try { scope = ScopeOf(auth.Value, workspaceId); }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }

                await deletion.DeleteAsync(scope, ct);
                return Results.Accepted();
            });
    }

    private static WorkspaceScope ScopeOf(BasicAuthOptions auth, string workspaceId)
        => WorkspaceScope.Current(auth, workspaceId);

    private static bool ValidIntentId(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl);

    internal sealed record ProblemReportInput(string? IntentId, string? Message);
}
