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

        state.ApplySecretCredential("pat", new SealedSecret { Cipher = "sealed-token", KeyId = "k1" });
        state.Status = ConnectionStatus.Connected;
        Assert.True(state.IsConfigured);

        var appRecipient = SharedRecipient.ForApp("alice", "apps/triage");
        Assert.True(state.TryAddSharedRecipient(appRecipient));
        Assert.False(state.TryAddSharedRecipient(appRecipient));
        Assert.True(state.IsSharedWith(appRecipient));
        Assert.True(state.TryRemoveSharedRecipient(appRecipient));
        Assert.False(state.IsSharedWith(appRecipient));
    }
}
