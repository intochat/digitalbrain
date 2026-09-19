using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";
    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public void Configure(ISiloBuilder silo) { }

    public void Configure(IEndpointRouteBuilder endpoints)
        => endpoints.MapJsonWebhook<GmailWatchPush>("/google/gmail/watch", async (push, grains, ct) =>
        {
            await grains.GetGrain<IGmail>(push.EmailAddress).AcceptWatchPush(push);
            return Results.Accepted();
        });
}
