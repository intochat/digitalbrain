using DigitalBrain.Platform.Contracts.Auth;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Sdk;
using DigitalBrain.Platform.Contracts.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Google.Gmail;

[ModuleId("gmail")]
public sealed class GmailModule : IModule<GmailModuleOptions>, IHttpModule
{
    public static IntegrationDefinition Integration { get; } = IntegrationDefinition.For("gmail", "Gmail")
        .RequiresSecret("ClientId")
        .RequiresSecret("ClientSecret")
        .RequiresSetting("PublicOrigin");

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(silo.Configuration.GetModuleOptions<GmailModuleOptions>("gmail"));
        services.TryAddSingleton<GmailRegistration>();
        services.TryAddSingleton<GmailLogins>();
        services.AddSingleton<BrowserLogins>(s => s.GetRequiredService<GmailLogins>());
        services.TryAddSingleton<IGmailTokenExchange, GmailTokenExchange>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<GmailLogins>()));
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapJsonWebhook<GmailPubSubPush>("/google/gmail/watch", async (envelope, grains, ct) =>
        {
            if (!GmailPubSub.TryUnwrap(envelope, out var push))
            {
                return Results.BadRequest();
            }

            await grains.GetGrain<IGmail>(push.EmailAddress).AcceptWatchPush(push);
            return Results.Accepted();
        });
        endpoints.MapGet("/google/gmail/oauth/callback", async (string? code, GmailRegistration registration, IGrainFactory grains, DigitalBrain.Platform.Contracts.Identity.IIdentity identity) =>
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest();
            }

            if (GmailRegistration.Explain(await registration.ReadAsync()) is { } unavailable)
            {
                return Results.Json(unavailable, statusCode: StatusCodes.Status409Conflict);
            }

            var owner = identity.CurrentPrincipal?.PrincipalId;
            await grains.GetGrain<IGmail>("gmail").AcceptAuthorizationCode(code, owner);
            return Results.Ok();
        });
    }
}
