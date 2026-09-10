using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddSingleton<TokenHandoff>();
        services.AddSingleton<GmailDraftAccess>();
        services.AddSingleton(settings);
        services.AddSingleton<GmailLogins>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<GmailLogins>()));
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.AddSingleton<IGmailProvider, FakeGmailProvider>();
            services.AddSingleton<IGmailTokenExchange, FakeGmailTokenExchange>();
        }
        else
        {
            services.AddSingleton<IGmailProvider, GmailMcpProvider>();
            services.AddSingleton<IGmailTokenExchange, GmailTokenExchange>();
        }
        services.AddSingleton<GmailTokenRefresh>();
        services.AddNativeTool("search_gmail_threads", services => services.GetRequiredService<GmailNativeTools>().CreateSearchThreads());
        services.AddNativeTool("read_gmail_thread", services => services.GetRequiredService<GmailNativeTools>().CreateGetThread());
        services.AddNativeTool("list_gmail_labels", services => services.GetRequiredService<GmailNativeTools>().CreateListLabels());
        services.AddNativeTool("gmail_current_account", services => services.GetRequiredService<GmailNativeTools>().CreateGetCurrentAccount());
        services.AddNativeTool("prepare_gmail_draft", services => services.GetRequiredService<GmailNativeTools>().CreateDraftPreview());
        services.AddSingleton(static services => new GmailNativeTools(
            services.GetRequiredService<IGrainFactory>().GetGrain<IGmail>(new NeuronId("gmail", "gmail").ToGrainId()),
            services.GetRequiredService<GmailLogins>(),
            services.GetRequiredService<TimeProvider>()));
        services.AddGmailAuthentication(settings, GmailLogins.LoginDefinition);
    }
}
