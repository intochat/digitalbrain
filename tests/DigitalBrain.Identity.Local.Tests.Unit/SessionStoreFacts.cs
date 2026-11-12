namespace DigitalBrain.Identity.Local.Tests.Unit;

using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Identity.Model;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Testing;
using Xunit;

public sealed class SessionStoreFacts
{
    [Fact]
    public async Task SessionsNeuronStartsFindsAndEndsSessions()
    {
        await using var os = TestOS.Local();
        os.Register<SessionsNeuron>();

        var brain = new BrainId("test-session-brain");
        var store = os.Get<ISessionStore>(ISessionStore.Id(brain.Value));

        var rawToken = "test-token-val";
        var tokenHash = SessionBook.HashToken(rawToken);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(2);

        var record = await store.Start(tokenHash, brain.Value, "principal-1", expires);
        Assert.Equal("principal-1", record.PrincipalId);
        Assert.Equal(tokenHash, record.TokenHash);

        var validated = await store.Find(tokenHash);
        Assert.NotNull(validated);
        Assert.Equal("principal-1", validated.PrincipalId);

        // Ending the session removes it
        await store.End(tokenHash);
        var afterEnd = await store.Find(tokenHash);
        Assert.Null(afterEnd);
    }
}
