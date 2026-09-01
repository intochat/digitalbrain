using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    public const string GmailOAuthConfigurationRoot = "DigitalBrain:Google:Gmail:OAuth";

    public static readonly Uri GmailMcpEndpoint = new("https://gmailmcp.googleapis.com/mcp/v1");

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddSingleton<ICapabilityHandler, GmailSearchHandler>();
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddSingleton<IGmail, FakeGmail>();
            return;
        }

        var settings = new GmailOAuthConfiguration(builder.Configuration);
        services.AddSingleton(settings);
        services.AddSingleton<GmailConnections>();
        services.AddSingleton<GmailLogins>();
        services.AddSingleton<GmailMcp>();
        services.AddSingleton<GmailDraftPreviews>();
        services.AddSingleton<IUserActionSource>(static s => s.GetRequiredService<GmailLogins>());
        services.AddSingleton<IHttpSurface>(static s => new BrowserLoginSurface(s.GetRequiredService<GmailLogins>()));
        services.AddSingleton<ITrustedUserCommandHandler>(static s => s.GetRequiredService<GmailDraftPreviews>());
        services.AddSingleton<IAgentToolSource, GmailToolSource>();
        services.AddSingleton<IGmail, McpGmail>();
        services.AddHostedService<BrowserLoginWorker<GmailLogins>>();
        services.AddHostedService<GmailMaintenanceWorker>();
        services.AddGmailAuthentication(settings, GmailLogins.LoginDefinition);
    }
}
