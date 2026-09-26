using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailConnectedFacts
{
    [Fact]
    public async Task AuthorizationCodePublishesGmailConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<GmailModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IGmailTokenExchange>(new FakeGmailTokens()))
            .StartAsync(ct);
        var gmail = brain.Get<IGmail>("gmail");
        await using var connected = await brain.Observe<GmailConnected>(gmail, ct);
        await gmail.AcceptAuthorizationCode("fake-code");
        Assert.Equal("user@gmail.com", (await connected.NextAsync(ct: ct)).EmailAddress);
        var mailbox = brain.Get<IGmail>("user@gmail.com");
        Assert.True(await mailbox.IsMailboxConnected());
        await brain.DeactivateAsync(mailbox, ct);
        Assert.True(await mailbox.IsMailboxConnected());
        Assert.False(await gmail.IsMailboxConnected());
    }
}

internal sealed class FakeGmailTokens : IGmailTokenExchange
{
    public Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", null, GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));

    public Task<GmailTokenGrant> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", "refresh-token", GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));
}