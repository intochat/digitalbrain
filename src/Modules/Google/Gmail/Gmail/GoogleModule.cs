using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Google.Gmail;

[ModuleConfiguration(typeof(GmailConfigurationContract))]
[ModuleHosting("DigitalBrain.Google.Gmail.GmailModuleHosting, DigitalBrain.Modules.Google.Gmail.Aspire.Hosting")]
public sealed class GmailModule : IModule
{
    public static ModuleDefinition Define(GmailModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PublicOrigin is { IsAbsoluteUri: false }) { throw new ArgumentException("Google PublicOrigin must be absolute.", nameof(options)); }
        if (!options.TokenEndpoint.IsAbsoluteUri || options.TokenEndpoint.Scheme is not ("http" or "https"))
        { throw new ArgumentException("Google token endpoint must be an absolute HTTP URL.", nameof(options)); }
        return new(typeof(GmailModule), new Dictionary<string, string?>
        {
            [GmailOAuthConfigurationRoot + ":PublicOrigin"] = options.PublicOrigin?.AbsoluteUri ?? "",
            [GmailOAuthConfigurationRoot + ":TokenEndpoint"] = options.TokenEndpoint.AbsoluteUri,
            [GmailOAuthConfigurationRoot + ":HostGmail"] = options.HostGmail.ToString(),
        });
    }
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
        services.AddDataProtection();
        services.TryAddSingleton<GmailSecrets>();
        services.TryAddSingleton<IGmailTokenExchange, GmailTokenExchange>();
        services.TryAddSingleton<IGmailMailbox, GmailMailboxClient>();
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

            await grains.GetGrain<IGmail>("gmail").AcceptAuthorizationCode(code);
            return Results.Ok();
        });
    }
}