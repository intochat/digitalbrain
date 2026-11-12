using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

public sealed class ConnectionKeysFacts
{
    [Fact]
    public void ForThenParseRoundTrips()
    {
        var key = ConnectionKeys.For("b", "gmail", "1");
        Assert.Equal("b/connection/gmail/1", key);
        Assert.Equal(("b", "gmail", "1"), ConnectionKeys.Parse(key));
    }

    [Fact]
    public void ConnectionAddress_and_SharedRecipient_encapsulate_addressing_and_sharing()
    {
        var address = ConnectionAddress.Create("alice", "github", "work");
        Assert.Equal("alice/connection/github/work", address.Key);
        Assert.Equal("neuron://alice/connection/github/work", address.UriString);
        Assert.Equal(address, ConnectionAddress.Parse(address.UriString));

        var state = new ConnectionState();
        Assert.False(state.IsConfigured);

        var configured = state.WithSecret("pat", new SealedSecret { Cipher = "sealed-token", KeyId = "k1" })
            .In(ConnectionStatus.Connected);
        Assert.True(configured.IsConfigured);

        var appRecipient = SharedRecipient.ForApp("alice", "apps/triage");
        var shared = configured.WithSharing(appRecipient.Value);
        Assert.True(shared.IsSharedWith(appRecipient));
        var sharedAgain = shared.WithSharing(appRecipient.Value);
        Assert.Same(shared, sharedAgain);
        var unshared = shared.WithoutSharing(appRecipient.Value);
        Assert.False(unshared.IsSharedWith(appRecipient));
    }
}
