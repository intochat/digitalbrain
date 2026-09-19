using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";
    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TokenHandoff>();
        services.AddOptions<GmailOAuthOptions>().Bind(silo.Configuration.GetSection(GmailOAuthOptions.SectionName));
        services.TryAddSingleton(static services => new GmailOAuthConfiguration(services.GetRequiredService<IOptions<GmailOAuthOptions>>()));
        services.TryAddSingleton<GmailLogins>();
        services.TryAddSingleton<IGmailTokenExchange, GmailTokenExchange>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<GmailLogins>()));
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapJsonWebhook<GmailWatchPush>("/google/gmail/watch", async (push, grains, ct) =>
        {
            await grains.GetGrain<IGmail>(push.EmailAddress).AcceptWatchPush(push);
            return Results.Accepted();
        });
        endpoints.MapGet("/google/gmail/oauth/callback", async (string? code, IGrainFactory grains) =>
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return Results.BadRequest();
            }

            await grains.GetGrain<IGmail>("gmail").AcceptAuthorizationCode(code);
            return Results.Ok();
        });
    }
}
