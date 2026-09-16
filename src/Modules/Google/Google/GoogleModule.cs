using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.Options;
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
        services.TryAddSingleton<TokenHandoff>();
        services.AddSingleton<GmailDraftAccess>();
        services.AddOptions<GmailOAuthOptions>().Bind(builder.Configuration.GetSection(GmailOAuthOptions.SectionName));
        services.AddSingleton(services => new GmailOAuthConfiguration(services.GetRequiredService<IOptions<GmailOAuthOptions>>()));
        services.AddSingleton<GmailLogins>();
        services.AddSingleton<IHttpSurface>(static services => new BrowserLoginSurface(services.GetRequiredService<GmailLogins>()));
        services.AddSingleton<IGmailProvider, GmailMcpProvider>();
        services.AddSingleton<IGmailTokenExchange, GmailTokenExchange>();
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
        services.AddGmailAuthentication(GmailLogins.LoginDefinition);
    }
}
