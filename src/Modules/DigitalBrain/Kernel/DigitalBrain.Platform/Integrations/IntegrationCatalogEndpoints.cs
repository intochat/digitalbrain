using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

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

internal static class IntegrationOperatorGate
{
    // Mirrors Identity's single-operator login; the Sdk cannot reference Identity.
    private const string OpenPostureLogin = "owner";

    // Deployment registrations belong to the operator; a per-account Owner role is not that.
    public static bool Allows(CallerContext? caller, IConfiguration configuration)
    {
        if (caller is null || caller.Kind != CallerKind.User || caller.StampedBy != TrustedEdge.AuthenticatedHttp)
        {
            return false;
        }

        var username = configuration["DigitalBrain:Auth:Username"];
        var hasCredential = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(configuration["DigitalBrain:Auth:Password"]);
        return string.Equals(caller.PrincipalId, hasCredential ? username : OpenPostureLogin, StringComparison.Ordinal);
    }
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

        group.MapPost("/{id}/registration", async (string id, ConfigureRegistrationInput body, [FromServices] IConfiguration configuration,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOperatorGate.Allows(CallerContextStamper.TryGet(out var caller) ? caller : null, configuration)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
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

        group.MapDelete("/{id}/registration/{field}", async (string id, string field, [FromServices] IConfiguration configuration,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOperatorGate.Allows(CallerContextStamper.TryGet(out var caller) ? caller : null, configuration)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
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
