using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Tests.Ino;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Google;

public class GmailInoToolProviderTests : NeuronTestBase
{
    private readonly RecordingGmailApiClient _gmail = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<IGmailApiClientFactory>(new TestGmailApiClientFactory(_gmail));
        });

    [Fact]
    public async Task BuildTools_returns_one_gated_gmail_tool()
    {
        var provider = new GmailInoToolProvider(Cluster.GrainFactory);

        var tools = provider.BuildTools("session-gmail-tool", CancellationToken.None);

        var tool = Assert.Single(tools);
        Assert.Equal("gmail_get_messages", tool.Name);
    }

    [Fact]
    public async Task Tool_returns_unauthorized_message_and_never_calls_gmail_api_when_not_connected()
    {
        var provider = new GmailInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools("session-gmail-tool-unauth", CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "last", ["maxResults"] = 3 }),
            CancellationToken.None);

        Assert.Contains("Google", result?.ToString());
        Assert.Empty(_gmail.ListCalls);
    }

    [Fact]
    public async Task Tool_calls_gmail_api_and_returns_enriched_content_when_connected()
    {
        const string clientId = "session-gmail-tool-auth";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("gmail-tool-user", "correct horse battery staple", clientId));

        var config = Grain<IGoogleConfigWriter>("google-config-writer");
        await config.StoreGoogleCredentialAsync();

        var provider = new GmailInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools(clientId, CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "last", ["maxResults"] = 3 }),
            CancellationToken.None);

        Assert.Contains("Gmail:", result?.ToString());
        Assert.Single(_gmail.ListCalls);
    }
}
