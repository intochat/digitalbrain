using System.Text.Json;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Invocation;

namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

public sealed class PersistedStateFacts
{
    [Fact]
    public void PersistedConnectionStateRoundTripsAcrossTheMove()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "connection-state-0.12.json");
        var json = File.ReadAllText(path);

        var state = JsonSerializer.Deserialize<ConnectionState>(json, RuntimeProtocol.Json);
        Assert.NotNull(state);
        Assert.Equal("oauth", state.AuthMethodId);
        Assert.Equal(ConnectionStatus.Connected, state.Status);
        Assert.Equal("alice@example.com", state.Account);
        Assert.Equal("read write", state.Scopes);
        Assert.Equal(["brain-2/app-1"], state.SharedInto);
        Assert.Equal(new SealedSecret { KeyId = "mk1", Cipher = "cipher-access" }, state.Secrets["access"]);
        Assert.Equal(new SealedSecret { KeyId = "mk1", Cipher = "cipher-refresh" }, state.Secrets["refresh"]);

        var reencoded = JsonSerializer.Serialize(state, RuntimeProtocol.Json);
        var roundTripped = JsonSerializer.Deserialize<ConnectionState>(reencoded, RuntimeProtocol.Json);
        Assert.NotNull(roundTripped);
        Assert.Equal(ConnectionStatus.Connected, roundTripped.Status);
        Assert.Equal("alice@example.com", roundTripped.Account);
        Assert.Equal(new SealedSecret { KeyId = "mk1", Cipher = "cipher-access" }, roundTripped.Secrets["access"]);
        Assert.Equal(new SealedSecret { KeyId = "mk1", Cipher = "cipher-refresh" }, roundTripped.Secrets["refresh"]);
    }
}
