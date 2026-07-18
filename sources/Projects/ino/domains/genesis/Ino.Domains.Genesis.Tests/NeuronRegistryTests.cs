using Ino.Core.Hosting;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Confirms the in-memory <see cref="INeuronRegistry"/> grain
/// round-trips registrations across activations on the test silo.
/// Volatile by design (issue #22 will make it durable); these tests pin
/// the in-process semantics so a future durability change has a
/// regression net.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class NeuronRegistryTests(GenesisTestSiloFixture fixture)
{
    [Fact]
    public async Task Register_then_get_round_trips()
    {
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var ct = TestContext.Current.CancellationToken;

        await registry.RegisterAsync("genesis.echo", "return NeuronResult.Ok(\"hi\");", ct);
        var body = await registry.GetScriptBodyAsync("genesis.echo", ct);

        Assert.Equal("return NeuronResult.Ok(\"hi\");", body);
    }

    [Fact]
    public async Task Get_returns_null_when_unregistered()
    {
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var ct = TestContext.Current.CancellationToken;

        var body = await registry.GetScriptBodyAsync("genesis.never-registered-" + Guid.NewGuid(), ct);

        Assert.Null(body);
    }

    [Fact]
    public async Task Re_register_replaces_prior_body()
    {
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var ct = TestContext.Current.CancellationToken;

        await registry.RegisterAsync("genesis.replaceable", "v1", ct);
        await registry.RegisterAsync("genesis.replaceable", "v2", ct);
        var body = await registry.GetScriptBodyAsync("genesis.replaceable", ct);

        Assert.Equal("v2", body);
    }

    [Fact]
    public async Task List_includes_registered_ids()
    {
        var registry = fixture.Grains.GetGrain<INeuronRegistry>(0);
        var ct = TestContext.Current.CancellationToken;

        var unique = $"genesis.list-{Guid.NewGuid():n}";
        await registry.RegisterAsync(unique, "return NeuronResult.Ok();", ct);
        var ids = await registry.ListAsync(ct);

        Assert.Contains(unique, ids);
    }
}
