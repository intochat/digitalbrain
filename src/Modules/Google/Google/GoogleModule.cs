using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";
    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public void Configure(ISiloBuilder silo) { }
}
