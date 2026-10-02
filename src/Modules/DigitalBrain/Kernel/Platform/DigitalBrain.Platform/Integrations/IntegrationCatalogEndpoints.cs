using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

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

        group.MapPost("/{id}/registration", async (string id, ConfigureRegistrationInput body, [FromServices] Microsoft.Extensions.Options.IOptions<Identity.Configuration.AuthOptions> auth,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOperatorGate.Allows(CallerContextStamper.TryGet(out var caller) ? caller : null, auth.Value)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
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

        group.MapDelete("/{id}/registration/{field}", async (string id, string field, [FromServices] Microsoft.Extensions.Options.IOptions<Identity.Configuration.AuthOptions> auth,
            [FromServices] IReadOnlyList<IntegrationDefinition> definitions, [FromServices] IGrainFactory grains) =>
        {
            if (!IntegrationOperatorGate.Allows(CallerContextStamper.TryGet(out var caller) ? caller : null, auth.Value)) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
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
