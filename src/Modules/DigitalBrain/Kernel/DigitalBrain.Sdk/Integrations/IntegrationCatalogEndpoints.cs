using System.Security.Claims;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Sdk.Integrations;

internal sealed record IntegrationCatalogEntry(string Id, string DisplayName, string Status, string[] MissingFields);

internal sealed record ConfigureRegistrationInput(Dictionary<string, string>? Values);

internal static class IntegrationCatalog
{
    public static async Task<IntegrationCatalogEntry[]> ListAsync(
        IReadOnlyList<IntegrationDefinition> definitions, IGrainFactory grains, CancellationToken cancellationToken)
    {
        var entries = new List<IntegrationCatalogEntry>(definitions.Count);
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await grains.GetGrain<IIntegrationRegistration>(IntegrationVault.GrainKeyPrefix + definition.Id).Read();
            entries.Add(new IntegrationCatalogEntry(definition.Id, definition.DisplayName, snapshot.Status.ToString(), snapshot.MissingFields));
        }

        return [.. entries];
    }
}

// Writing a deployment-wide provider registration takes the Owner role from the signed-in session.
internal static class IntegrationOwnerGate
{
    // Identity signs sessions in with ClaimTypes.Role = MemberRole.Owner.ToString(); the Sdk cannot
    // reference the Identity contracts, so the role name is matched here.
    public const string OwnerRole = "Owner";

    public static bool Allows(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && user.IsInRole(OwnerRole);
}

internal static class IntegrationCatalogEndpoints
{
    internal static void MapIntegrations(this IEndpointRouteBuilder endpoints)
    {
        // Deliberately not brain-scoped: registrations are deployment info, not workspace data.
        var group = endpoints.MapGroup("/integrations");
        group.AddEndpointFilter(async (context, next) =>
            CallerContextStamper.TryGet(out _) ? await next(context) : Results.Unauthorized());

        group.MapGet("", async ([FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains, CancellationToken cancellationToken) =>
            Results.Ok(await IntegrationCatalog.ListAsync(definitions, grains, cancellationToken)));

        group.MapPost("/{id}/registration", async (string id, ConfigureRegistrationInput body, HttpContext http,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOwnerGate.Allows(http.User)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            if (!definitions.Any(definition => definition.Id == id)) { return Results.NotFound(); }
            try
            {
                var registration = grains.GetGrain<IIntegrationRegistration>(IntegrationVault.GrainKeyPrefix + id);
                return Results.Ok(await registration.Configure(new ConfigureRegistration { Values = body.Values ?? [] }));
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });

        group.MapDelete("/{id}/registration/{field}", async (string id, string field, HttpContext http,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOwnerGate.Allows(http.User)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
            if (!definitions.Any(definition => definition.Id == id)) { return Results.NotFound(); }
            try
            {
                return Results.Ok(await grains.GetGrain<IIntegrationRegistration>(IntegrationVault.GrainKeyPrefix + id).Clear(field));
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
        });
    }
}
