using System.Text.Json;
using Brain.Contracts;
using Brain.Kernel;
using Brain.Kernel.Connections;
using Brain.Modules.Google;
using DigitalBrain.Tests;
using Google.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.TestingHost;
using Xunit;

namespace Brain.KernelTests;

public sealed class GoogleOAuthSecurityTests(BrainClusterFixture<GoogleSecurityKindsConfigurator> fixture)
    : BrainTest<GoogleSecurityKindsConfigurator>(fixture)
{
    [Fact]
    public void Runtime_registration_rejects_partial_configuration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Google:ClientId"] = "client-only"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TestSiloBuilder().AddDigitalBrainGoogle(config, new TestEnvironment(Environments.Production)));

        Assert.DoesNotContain("client-only", exception.Message);
    }

    [Fact]
    public void Production_rejects_non_https_redirect_uri()
    {
        var config = CompleteConfiguration("http://example.com/oauth/callback/google");

        Assert.Throws<InvalidOperationException>(() =>
            new TestSiloBuilder().AddDigitalBrainGoogle(config, new TestEnvironment(Environments.Production)));
    }

    [Fact]
    public async Task Tampered_and_replayed_oauth_state_never_exchange_twice()
    {
        GoogleSecurityKindsConfigurator.GoogleConnectionProvider.Reset();
        var connection = Neuron("connection", $"google-{Guid.NewGuid():N}");
        var start = await connection.InvokeAsync(new(
            "connection.start-auth.v1", "{}", CommandId(), OwnerSession));
        var state = StateFrom(start.OutputJson);

        var tampered = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new(
                "connection.complete-auth.v1",
                JsonSerializer.Serialize(new { code = "code", state = state + "x" }),
                CommandId(),
                OwnerSession)));
        Assert.Equal(BrainErrors.ConnectionUnhealthy, tampered.Code);
        Assert.Equal(0, GoogleSecurityKindsConfigurator.GoogleConnectionProvider.ExchangeCodeCalls);

        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "code", state }),
            CommandId(),
            OwnerSession));
        var replay = await Assert.ThrowsAsync<BrainException>(() =>
            connection.InvokeAsync(new(
                "connection.complete-auth.v1",
                JsonSerializer.Serialize(new { code = "code", state }),
                CommandId(),
                OwnerSession)));

        Assert.Equal(BrainErrors.ConnectionUnhealthy, replay.Code);
        Assert.Equal(1, GoogleSecurityKindsConfigurator.GoogleConnectionProvider.ExchangeCodeCalls);
    }

    [Fact]
    public async Task New_send_contract_decline_replay_success_and_delivery_unknown_use_effect_rail()
    {
        GoogleSecurityKindsConfigurator.GmailProvider.Reset();
        var gmailId = $"outbox-{Guid.NewGuid():N}";
        await EnsureConnectedAsync(gmailId);
        var gmail = Neuron("gmail", gmailId);

        var declinedProposal = await ProposeAsync(gmail, "declined-operation");
        var declinedEffect = Cluster.GrainFactory.GetGrain<INeuron>(declinedProposal.EffectKey!);
        await declinedEffect.InvokeAsync(new("effect.decline.v1", "{}", CommandId(), OwnerSession));
        var declined = await Assert.ThrowsAsync<BrainException>(() =>
            ExecuteAsync(gmail, declinedProposal.EffectKey!, CommandId()));
        Assert.Equal(BrainErrors.EffectNotApproved, declined.Code);
        Assert.Equal(0, GoogleSecurityKindsConfigurator.GmailProvider.SendCalls);

        var successfulProposal = await ProposeAsync(gmail, "successful-operation");
        var successfulEffect = Cluster.GrainFactory.GetGrain<INeuron>(successfulProposal.EffectKey!);
        await successfulEffect.InvokeAsync(new("effect.approve.v1", "{}", CommandId(), OwnerSession));
        var executeCommand = CommandId();
        var sent = await ExecuteAsync(gmail, successfulProposal.EffectKey!, executeCommand);
        var replay = await ExecuteAsync(gmail, successfulProposal.EffectKey!, executeCommand);
        Assert.Equal(sent, replay);
        Assert.Contains("fake-message-id", sent.OutputJson);
        Assert.Equal(1, GoogleSecurityKindsConfigurator.GmailProvider.SendCalls);

        GoogleSecurityKindsConfigurator.GmailProvider.SendException = new TimeoutException("ambiguous delivery");
        var unknownProposal = await ProposeAsync(gmail, "unknown-operation");
        var unknownEffect = Cluster.GrainFactory.GetGrain<INeuron>(unknownProposal.EffectKey!);
        await unknownEffect.InvokeAsync(new("effect.approve.v1", "{}", CommandId(), OwnerSession));
        var unknown = await ExecuteAsync(gmail, unknownProposal.EffectKey!, CommandId());

        Assert.Contains("delivery-unknown", unknown.OutputJson);
        Assert.Contains(
            (await gmail.ReadEventsAsync(0, 100)).Events,
            entry => entry.Kind == "gmail.delivery-unknown");
        Assert.Equal(2, GoogleSecurityKindsConfigurator.GmailProvider.SendCalls);
    }

    private async Task EnsureConnectedAsync(string gmailId)
    {
        var connection = Neuron("connection", "google-primary");
        var start = await connection.InvokeAsync(new(
            "connection.start-auth.v1", "{}", CommandId(), OwnerSession));
        var state = StateFrom(start.OutputJson);
        await connection.InvokeAsync(new(
            "connection.complete-auth.v1",
            JsonSerializer.Serialize(new { code = "auth-code", state }),
            CommandId(),
            OwnerSession));
        await connection.InvokeAsync(new(
            "neuron.grant.v1",
            JsonSerializer.Serialize(new
            {
                granteeKey = AddressKey("gmail", gmailId),
                contract = "connection.lease-token.v1"
            }),
            CommandId(),
            OwnerSession));
    }

    private static Task<NeuronReceipt> ProposeAsync(INeuron gmail, string operation) =>
        gmail.InvokeAsync(new(
            GoogleCapabilityIds.GmailSendPropose,
            JsonSerializer.Serialize(new GmailSendProposalRequest(
                "a@example.com",
                "subject",
                "body",
                operation)),
            CommandId(),
            "owner|actor/test|session/t"));

    private static Task<NeuronReceipt> ExecuteAsync(INeuron gmail, string effectKey, string commandId) =>
        gmail.InvokeAsync(new(
            GoogleCapabilityIds.GmailSendExecute,
            JsonSerializer.Serialize(new GmailSendExecutionRequest(effectKey)),
            commandId,
            "owner|actor/test|session/t"));

    private static IConfiguration CompleteConfiguration(string redirectUri) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Google:ClientId"] = "client-id",
                ["DigitalBrain:Google:ClientSecret"] = "client-secret",
                ["DigitalBrain:Google:RedirectUri"] = redirectUri
            })
            .Build();

    private static string StateFrom(string outputJson)
    {
        using var output = JsonDocument.Parse(outputJson);
        var authorizationUrl = output.RootElement.GetProperty("authorizationUrl").GetString()!;
        return new Uri(authorizationUrl).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => Uri.UnescapeDataString(pair[0]) == "state")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .Single();
    }

    private static string CommandId() => Guid.NewGuid().ToString("N");

    private sealed class TestSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "DigitalBrain.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class GoogleSecurityKindsConfigurator : ISiloConfigurator
{
    public static FakeConnectionProvider GoogleConnectionProvider { get; } = new();
    public static FakeGmailProvider GmailProvider { get; } = new();

    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddBrainKernel();
        siloBuilder.Services.AddKeyedSingleton<IConnectionProvider>(
            "google",
            GoogleConnectionProvider);
        siloBuilder.Services.AddKeyedSingleton<IGmailProvider>("google", GmailProvider);
        siloBuilder.AddBrainKind(
            "gmail",
            services => new GmailKind(
                services.GetRequiredService<IGrainFactory>(),
                services));
    }
}
