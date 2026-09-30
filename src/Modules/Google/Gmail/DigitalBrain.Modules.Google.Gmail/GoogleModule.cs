using DigitalBrain.Core.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Google.Gmail;

public sealed class GmailModule : IModule<GmailModuleOptions>
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TokenHandoff>();
        var moduleOptions = silo.Configuration.GetModuleOptions<GmailModuleOptions>(nameof(GmailModule));
        // ClientId and ClientSecret keep their private DigitalBrain:Google:Gmail:OAuth path; the declared origin and token endpoint arrive as module options.
        services.AddOptions<GmailOAuthOptions>().Bind(silo.Configuration.GetSection(GmailOAuthOptions.SectionName))
            .Configure(oauth =>
            {
                if (moduleOptions.PublicOrigin is { } origin) { oauth.PublicOrigin = origin.AbsoluteUri; }
                oauth.TokenEndpoint = moduleOptions.TokenEndpoint.AbsoluteUri;
            });
        services.TryAddSingleton(static services => new GmailOAuthConfiguration(services.GetRequiredService<IOptions<GmailOAuthOptions>>()));
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
        endpoints.MapGet("/google/gmail/oauth/callback", async (string? code, IGrainFactory grains) =>
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest();
            }

            var owner = DigitalBrain.Core.Enforcement.CallerContextStamper.TryGet(out var caller)
                ? caller.PrincipalId
                : null;
            await grains.GetGrain<IGmail>("gmail").AcceptAuthorizationCode(code, owner);
            return Results.Ok();
        });
    }
}