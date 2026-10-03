using DigitalBrain.Contracts.Integrations;
using DigitalBrain.Kernel;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Testing.Unit;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Platform.Tests.Unit.Integrations;

public sealed class ScriptAccountFacts
{
    [Fact]
    public async Task RequestingAKnownKindReturnsAnOpaqueRefAndAnUnknownKindIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresKindModule>().StartAsync(ct);
        var accounts = brain.Get<IConnectionRequests>("brain-1");

        var kind = Assert.Single(await accounts.ListKinds());
        Assert.Equal("postgres", kind.Id);
        Assert.Equal("Postgres", kind.DisplayName);
        var account = await accounts.Request("postgres");
        Assert.Equal("postgres", account.Kind);
        Assert.False(string.IsNullOrWhiteSpace(account.Id));
        Assert.Equal(account, Assert.Single(await accounts.ListPending()));
        await Assert.ThrowsAsync<ArgumentException>(() => accounts.Request("missing"));
    }

    public sealed class PostgresKindModule : IModule
    {
        public static IntegrationDefinition Integration { get; } = IntegrationDefinition.For("postgres", "Postgres");

        public void Configure(ISiloBuilder silo) { }
    }
}
