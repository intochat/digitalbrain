using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Auth;

[Collection("kernel-host")]
public sealed class UserSessionNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task First_Login_Provisions_Local_User_And_Creates_Session()
    {
        var session = Grain<IUserSessionNeuron>("session-auth-valid");

        await session.FireAsync(new LoginRequest("alice.local", "correct horse battery staple", "test"));

        var timeline = await session.GetOutgoingTimelineAsync();
        var registered = Assert.Single(timeline.OfType<LocalUserRegistered>());
        Assert.Equal("alice.local", registered.Username);
        Assert.DoesNotContain("correct horse", registered.PasswordHashBase64, StringComparison.OrdinalIgnoreCase);

        var created = Assert.Single(timeline.OfType<UserSessionCreated>());
        var state = await session.GetSessionAsync(created.SessionId);

        Assert.NotNull(state);
        Assert.True(state!.Active);
        Assert.Equal("alice.local", state.UserId.Value);
        Assert.Contains("admin", state.Roles);
        Assert.Contains(timeline.OfType<LoginSucceeded>(), s => s.SessionId == created.SessionId);
    }

    [Fact]
    public async Task Invalid_Password_Fires_LoginFailed_And_Does_Not_Create_Second_Session()
    {
        var session = Grain<IUserSessionNeuron>("session-auth-invalid");

        await session.FireAsync(new LoginRequest("bob", "first-password", "test"));
        await session.FireAsync(new LoginRequest("bob", "wrong-password", "test"));

        var timeline = await session.GetOutgoingTimelineAsync();

        Assert.Single(timeline.OfType<UserSessionCreated>());
        var failed = Assert.Single(timeline.OfType<LoginFailed>());
        Assert.Equal("bob", failed.Username);
        Assert.Equal("invalid username or password", failed.Reason);
    }

    [Fact]
    public async Task Logout_Ends_Existing_Session()
    {
        var session = Grain<IUserSessionNeuron>("session-auth-logout");

        await session.FireAsync(new LoginRequest("carol", "first-password", "test"));
        var created = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single();

        await session.FireAsync(new LogoutRequest(created.SessionId, "test"));

        Assert.Null(await session.GetSessionAsync(created.SessionId));
        var timeline = await session.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<UserSessionEnded>(), e => e.SessionId == created.SessionId);
    }

    [Fact]
    public async Task Login_Surface_Is_Server_Driven_Form()
    {
        var session = Grain<IUserSessionNeuron>("session-auth-surface");

        var surface = await session.BuildLoginSurfaceAsync("test-client");

        Assert.Equal(UiSurfaceKinds.Login, surface.Kind);
        Assert.Equal(true, surface.Props[UiSurfaceKeys.RequiresInput]);
        Assert.Equal("test-client", surface.Props["clientId"]);
        AssertSynapseAction(surface.Props["submitAction"], nameof(LoginRequest));

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var form = Assert.Single(FindNodes(tree), node => node.Type == NeuronUiKit.Form);
        Assert.Equal(nameof(LoginRequest), form.Props[UiSurfaceKeys.SynapseType]);
        Assert.Equal("test-client", form.Props["clientId"]);
    }

    // Proves the actual bug this task's plan fixes: after a real login, the signed-in shell surface
    // reaches the connecting client's own HomeFeedBus stream (addressed by its clientId), not merely a
    // Props dictionary that happens to carry the right value with nothing actually routing on it. Follows
    // the direct-subscribe pattern from HomeFeedCrossSiloTests.cs/GatewayServiceTests.cs rather than driving
    // the gRPC-facing WatchHomeFeed surface.
    [Fact]
    public async Task Login_Broadcasts_Signed_In_Shell_Surface_To_The_Connecting_Clients_HomeFeedBus_Stream()
    {
        var session = Grain<IUserSessionNeuron>("session-shell-routing");
        var bus = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        const string clientId = "shell-routing-connection";
        await using var subscription = await bus.SubscribeAsync(clientId);

        await session.HandleAsync(new LoginRequest("shell-routing-user", "correct horse battery staple", clientId));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        RfwCard? shellCard = null;
        while (shellCard is null)
        {
            var card = await subscription.Reader.ReadAsync(cts.Token);
            if (string.Equals(card.CorrelationId, "surface.shell.shell-routing-user", StringComparison.Ordinal))
            {
                shellCard = card;
            }
        }

        Assert.Equal(clientId, shellCard.ClientId);
        Assert.Contains(clientId, shellCard.DataJson, StringComparison.Ordinal);
        Assert.Contains("\"workspaceId\":\"default\"", shellCard.DataJson, StringComparison.Ordinal);
        Assert.Contains("\"targetSurfaceKind\":\"workspace\"", shellCard.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registration_With_Slash_In_Username_Is_Rejected()
    {
        var session = Grain<IUserSessionNeuron>("session-auth-invalid-charset");

        await session.FireAsync(new LoginRequest("alice/bob", "some-password-123", "test"));

        var timeline = await session.GetOutgoingTimelineAsync();
        Assert.Empty(timeline.OfType<LocalUserRegistered>());
        var failed = Assert.Single(timeline.OfType<LoginFailed>());
        Assert.Equal("alice/bob", failed.Username);
        Assert.Contains("invalid characters", failed.Reason);
    }

    private static void AssertSynapseAction(object? value, string expectedSynapseType)
    {
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(value);
        Assert.Equal(expectedSynapseType, action[UiSurfaceKeys.SynapseType]);
        Assert.True(action.ContainsKey(UiSurfaceKeys.Props));
    }

    private static IEnumerable<UiWidgetTree> FindNodes(UiWidgetTree tree)
    {
        yield return tree;

        foreach (var child in tree.Children ?? [])
        {
            foreach (var found in FindNodes(child))
            {
                yield return found;
            }
        }
    }
}
