using DigitalBrain.Google.Gmail;
using DigitalBrain.MyData;
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
        await using var brain = await UnitTest.Create().WithModule<MyDataModule>().WithModule<GmailModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IGmailTokenExchange>(new FakeGmailTokens()))
            .StartAsync(ct);
        var gmail = brain.Get<IGmail>("gmail");
        await using var connected = await brain.Observe<GmailConnected>(gmail, ct);
        await gmail.AcceptAuthorizationCode("fake-code", "owner");
        Assert.Equal("user@gmail.com", (await connected.NextAsync(ct: ct)).EmailAddress);

        var export = await brain.Get<IVault>("owner").Export(UserCaller(), ct);
        var text = string.Join("\n", export.Fields.Select(field => $"{field.FieldPath}={field.Value}"));
        Assert.DoesNotContain("access-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token", text, StringComparison.Ordinal);
    }

    private static DigitalBrain.Contracts.Enforcement.CallerContext UserCaller() => new()
    {
        PrincipalId = "owner",
        AccountId = "owner",
        WorkspaceId = "owner",
        Kind = DigitalBrain.Contracts.Enforcement.CallerKind.User,
        StampedBy = DigitalBrain.Contracts.Enforcement.TrustedEdge.AuthenticatedHttp,
    };
}

internal sealed class FakeGmailTokens : IGmailTokenExchange
{
    public Task<GmailTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<GmailTokenGrant> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
        => Task.FromResult(new GmailTokenGrant("access-token", "refresh-token", GmailOAuthConfiguration.ReadScope, 3600, "user@gmail.com"));
}