namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

using DigitalBrain.Capabilities.Secrets;
using Xunit;

public sealed class ConnectionStateFacts
{
    [Fact]
    public void ToStringRedactsState()
    {
        var state = new ConnectionState
        {
            AuthMethodId = "secret-auth",
            Account = "alice@example.com",
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
            {
                ["key"] = new() { KeyId = "k1", Cipher = "super-secret-ciphertext" },
            },
        };

        var str = state.ToString();
        Assert.Equal("ConnectionState { <redacted> }", str);
        Assert.DoesNotContain("alice", str, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", str, StringComparison.Ordinal);
    }

    [Fact]
    public void PureTransitionsEvolveStateImmutably()
    {
        var initial = new ConnectionState();
        var val = new SealedSecret { KeyId = "k1", Cipher = "c1" };

        var (connected, factConnected) = initial.Connect("connector-1", "conn-1", "token", val, "alice@example.com");
        Assert.Equal(ConnectionStatus.Connected, connected.Status);
        Assert.Equal("alice@example.com", connected.Account);
        Assert.Equal("token", connected.AuthMethodId);
        Assert.True(connected.IsConfigured);
        Assert.Equal(ConnectionStatus.Disconnected, initial.Status); // Initial remains untouched!

        var connFact = Assert.IsType<ConnectionConnected>(factConnected);
        Assert.Equal("connector-1", connFact.ConnectorId);
        Assert.Equal("conn-1", connFact.ConnectionId);

        var (shared, factShared) = connected.Share("connector-1", "conn-1", "recipient-1");
        Assert.Contains("recipient-1", shared.SharedInto);
        Assert.Empty(connected.SharedInto);

        var (unshared, factUnshared) = shared.Unshare("connector-1", "conn-1", "recipient-1");
        Assert.DoesNotContain("recipient-1", unshared.SharedInto);

        var (disconnected, factDisc) = unshared.Disconnect("connector-1", "conn-1");
        Assert.Equal(ConnectionStatus.Disconnected, disconnected.Status);
        Assert.Empty(disconnected.Secrets);
        Assert.Null(disconnected.Account);
    }
}
