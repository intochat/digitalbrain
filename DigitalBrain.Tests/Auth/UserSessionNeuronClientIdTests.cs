using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Auth;

public class UserSessionNeuronClientIdTests : NeuronTestBase
{
    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_The_Session_Created_For_That_ClientId()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("clientid-user", "correct horse battery staple", "my-connection"));

        var resolved = await session.GetSessionByClientIdAsync("my-connection");

        Assert.NotNull(resolved);
        Assert.Equal("clientid-user", resolved!.UserId.Value);
    }

    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_The_Latest_Active_Session_For_That_ClientId()
    {
        var session = Grain<IUserSessionNeuron>("session-clientid-latest");
        const string clientId = "reused-connection";

        await session.HandleAsync(new LoginRequest("clientid-latest-user", "correct horse battery staple", clientId));
        var first = await session.GetSessionByClientIdAsync(clientId);
        Assert.NotNull(first);

        await session.HandleAsync(new LoginRequest("clientid-latest-user", "correct horse battery staple", clientId));
        var latest = await session.GetSessionByClientIdAsync(clientId);

        Assert.NotNull(latest);
        Assert.Equal("clientid-latest-user", latest!.UserId.Value);
        Assert.NotEqual(first!.SessionId, latest.SessionId);
    }

    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_Null_For_An_Unknown_ClientId()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        var resolved = await session.GetSessionByClientIdAsync("never-logged-in");
        Assert.Null(resolved);
    }

    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_Null_After_That_ClientIds_Session_Logged_Out()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("clientid-logout-user", "correct horse battery staple", "logout-connection"));
        var beforeLogout = await session.GetSessionByClientIdAsync("logout-connection");
        Assert.NotNull(beforeLogout);

        await session.HandleAsync(new LogoutRequest(beforeLogout!.SessionId, "logout-connection"));

        var afterLogout = await session.GetSessionByClientIdAsync("logout-connection");
        Assert.Null(afterLogout);
    }
}
