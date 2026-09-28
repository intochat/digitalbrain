using DigitalBrain.Contracts;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;
using DigitalBrain.Identity;

namespace IntoChat.Operations;

internal static class OperationsEndpoints
{
    public static void MapOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/workspaces/{workspaceId}/reports",
            async (string workspaceId, ProblemReportInput input, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
            {
                if (!WorkspaceScope.IsValidId(input.IntentId) || string.IsNullOrWhiteSpace(input.Message))
                {
                    return Results.BadRequest(new { error = "A report needs the intent id it came from and a message." });
                }

                try
                {
                    var scope = WorkspaceScope.Current(auth.Value, workspaceId);
                    var report = await brain.Get<IProblemReports>(scope.Id)
                        .Add(scope.WorkspaceId, input.IntentId!, input.Message!.Trim()).WaitAsync(ct);
                    return Results.Created($"/workspaces/{workspaceId}/reports", report);
                }
                catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
            });
    }

    internal sealed record ProblemReportInput(string? IntentId, string? Message);
}
