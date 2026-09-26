using DigitalBrain.Behaviors;
using DigitalBrain.Flutter.Inbox;
using DigitalBrain.Flutter.Inbox.Signals;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ImportantMailFacts
{
    [Fact]
    public async Task OnlyAnImportantSubjectReachesTheInbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var gmailApi = new ScriptedMailbox([new("m1", "Important: board"), new("m2", "newsletter")]);
        await using var brain = await UnitTest.Create().WithModule<GmailModule>().WithModule<DigitalBrain.Flutter.FlutterModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.Configure<GmailOAuthOptions>(options => options.TopicName = "projects/app/topics/gmail");
                silo.Services.AddSingleton<IGmailTokenExchange>(new FakeGmailTokens());
                silo.Services.AddSingleton<IGmailMailbox>(gmailApi);
            })
            .StartAsync(ct);
        await brain.Get<IGmail>("gmail").AcceptAuthorizationCode("fake-code");
        var mailbox = brain.Get<IGmail>("user@gmail.com");
        var inbox = brain.Get<IInbox>("ui");
        await using var run = brain.RunBehavior((live, token) => new ImportantMail(live, "user@gmail.com").RunAsync(token), ct);
        await using var appeared = await brain.Observe<InboxAppeared>(inbox, ct);
        await run.WaitForSubscriptionAsync<GmailMessageArrived>(mailbox, ct);
        await mailbox.AcceptWatchPush(new("200", "user@gmail.com"));
        Assert.Equal("Important: board", (await appeared.NextAsync(ct: ct)).Text);
        Assert.Single(await inbox.Read());
    }
}

internal sealed class ScriptedMailbox(IReadOnlyList<GmailIncoming> added) : IGmailMailbox
{
    public Task<GmailWatchReceipt> WatchAsync(string accessToken, string topicName, CancellationToken cancellationToken)
        => Task.FromResult(new GmailWatchReceipt("100", DateTimeOffset.UnixEpoch.AddDays(7)));

    public Task<IReadOnlyList<GmailIncoming>> ListAddedAsync(string accessToken, string startHistoryId, CancellationToken cancellationToken)
        => Task.FromResult(added);
}

internal sealed class FakeGmailTokens : IGmailTokenExchange
{
    public Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", null, GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));

    public Task<GmailTokenGrant> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", "refresh-token", GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));
}