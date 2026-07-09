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
    private readonly RecordingGmailApiClientFactory _gmailFactory;

    public GmailInoToolProviderTests()
    {
        _gmailFactory = new RecordingGmailApiClientFactory(_gmail);
    }

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<IGmailApiClientFactory>(_gmailFactory);
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

    [Fact]
    public async Task Tool_uses_logged_in_user_scope_when_google_credential_is_user_scoped()
    {
        const string userId = "gmail-tool-oauth-user";
        const string clientId = "session-gmail-tool-oauth-user";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest(userId, "correct horse battery staple", clientId));
        await StoreOAuthShapedGoogleCredentialAsync(userId);

        var provider = new GmailInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools(clientId, CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "last", ["maxResults"] = 3 }),
            CancellationToken.None);

        Assert.Contains("Gmail:", result?.ToString());
        Assert.Contains(_gmailFactory.Scopes, scope => scope.UserId.Value == userId);
        Assert.DoesNotContain(_gmailFactory.Scopes, scope => scope.UserId.Value == "gmail-capability-main");
    }

    private async Task StoreOAuthShapedGoogleCredentialAsync(string userId)
    {
        var store = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<IPackConfigStore>();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.ClientIdKey] = "client-id",
            [GoogleClientFactory.ClientSecretKey] = "client-secret",
            [GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri
        });
        await store.SetAsync(PackConfigScopes.ForUser(new UserId(userId)), GoogleClientFactory.PackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.RefreshTokenKey] = "refresh-token"
        });
    }
}

internal sealed class RecordingGmailApiClientFactory(RecordingGmailApiClient client) : IGmailApiClientFactory
{
    public List<NeuronScope> Scopes { get; } = [];

    public Task<IGmailApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Scopes.Add(scope);
        return Task.FromResult<IGmailApiClient>(client);
    }
}
