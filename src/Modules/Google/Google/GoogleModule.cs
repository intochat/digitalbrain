using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
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
        var settings = new GmailOAuthConfiguration(builder.Configuration);
        services.AddSingleton(settings);
        services.AddSingleton<GmailLogins>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<GmailLogins>()));
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddSingleton<IGmailProvider, FakeGmailProvider>();
        }
        else
        {
            services.AddSingleton<IGmailProvider, GmailMcpProvider>();
        }
        services.AddSingleton<GmailTokenRefresh>();
        // NativeTools contributor lands after the AI module merge
        services.AddSingleton(static services => new GmailNativeTools(
            services.GetRequiredService<IGrainFactory>().GetGrain<IGmail>(new NeuronId("gmail", "gmail").ToGrainId()),
            services.GetRequiredService<GmailLogins>()));
        services.AddGmailAuthentication(settings, GmailLogins.LoginDefinition);
    }
}
