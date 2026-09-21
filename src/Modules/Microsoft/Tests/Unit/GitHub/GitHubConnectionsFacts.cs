using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Microsoft.GitHub.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GitHubConnectionsFacts
{
    [Fact]
    public async Task RegisterAddsConnectionAndPublishesSignal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MicrosoftModule>()
            .StartAsync(ct);
        var connections = brain.Get<IGitHubConnections>("connections");
        await using var registered = await brain.Observe<GitHubConnectionRegistered>(connections, ct);
        var record = await connections.Register(new("conn-1", 7, 9, 11, "intochat", "digitalbrain", "epoch-1"));
        Assert.Equal("conn-1", record.Id);
        var published = await registered.NextAsync(ct: ct);
        Assert.Equal("conn-1", published.Record.Id);
        Assert.Equal("epoch-1", Assert.Single((await connections.List()).Connections).Epoch);
    }

    [Fact]
    public async Task RegisterReplacesAnExistingConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MicrosoftModule>()
            .StartAsync(ct);
        var connections = brain.Get<IGitHubConnections>("connections");
        await connections.Register(new("conn-1", 7, 9, 11, "intochat", "digitalbrain", "epoch-1"));
        await connections.Register(new("conn-1", 7, 9, 11, "intochat", "digitalbrain", "epoch-2"));
        Assert.Equal("epoch-2", Assert.Single((await connections.List()).Connections).Epoch);
    }

    [Fact]
    public async Task RegisterRejectsInvalidIdentifiers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<MicrosoftModule>()
            .StartAsync(ct);
        var connections = brain.Get<IGitHubConnections>("connections");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => connections.Register(new("conn-1", 0, 9, 11, "intochat", "digitalbrain", "epoch-1")));
        await Assert.ThrowsAsync<ArgumentException>(
            () => connections.Register(new("conn-1", 7, 9, 11, " ", "digitalbrain", "epoch-1")));
    }
}
