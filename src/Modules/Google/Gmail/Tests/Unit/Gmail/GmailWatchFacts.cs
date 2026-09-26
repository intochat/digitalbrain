using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailWatchFacts
{
    [Fact]
    public async Task LoginArmsTheWatchAndAPushPublishesTheSubject()
    {
        var ct = TestContext.Current.CancellationToken;
        var gmailApi = new ScriptedMailbox([new("m1", "Important: board"), new("m2", "newsletter")]);
        await using var brain = await UnitTest.Create().WithModule<GmailModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.Configure<GmailOAuthOptions>(options => options.TopicName = "projects/app/topics/gmail");
                silo.Services.AddSingleton<IGmailTokenExchange>(new FakeGmailTokens());
                silo.Services.AddSingleton<IGmailMailbox>(gmailApi);
            })
            .StartAsync(ct);
        var login = brain.Get<IGmail>("gmail");
        await login.AcceptAuthorizationCode("fake-code");
        Assert.Equal(["projects/app/topics/gmail"], gmailApi.WatchedTopics);
        var mailbox = brain.Get<IGmail>("user@gmail.com");
        await using var arrived = await brain.Observe<GmailMessageArrived>(mailbox, ct);
        await mailbox.AcceptWatchPush(new("200", "user@gmail.com"));
        Assert.Equal("Important: board", (await arrived.NextAsync(ct: ct)).Subject);
        Assert.Equal("newsletter", (await arrived.NextAsync(ct: ct)).Subject);
        Assert.Equal("100", gmailApi.StartHistoryId);
    }
}

internal sealed class ScriptedMailbox(IReadOnlyList<GmailIncoming> added) : IGmailMailbox
{
    public List<string> WatchedTopics { get; } = [];
    public string? StartHistoryId { get; private set; }

    public Task<GmailWatchReceipt> WatchAsync(string accessToken, string topicName, CancellationToken cancellationToken)
    {
        WatchedTopics.Add(topicName);
        return Task.FromResult(new GmailWatchReceipt("100", DateTimeOffset.UnixEpoch.AddDays(7)));
    }

    public Task<IReadOnlyList<GmailIncoming>> ListAddedAsync(string accessToken, string startHistoryId, CancellationToken cancellationToken)
    {
        StartHistoryId = startHistoryId;
        return Task.FromResult(added);
    }
}
