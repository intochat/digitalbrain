using DigitalBrain;
using DigitalBrain.Testing;

namespace APP_NAMESPACE.Tests.Integration;

public sealed class APP_CLASSRuntimeFixture : BothRuntimesFixture
{
    protected override IReadOnlyList<Type> Modules => [typeof(APP_CLASS)];
}

public sealed class APP_CLASSRuntimeFacts(APP_CLASSRuntimeFixture fixture) : IClassFixture<APP_CLASSRuntimeFixture>
{
    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task CounterSurvivesRestartOnBothRuntimes(NeuronRuntimeKind kind)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var runtime = await fixture.Start(kind, ct);
        var id = runtime.Id("APP_ID.counter", "starter");

        Assert.Equal(1, await runtime.Get<ICounter>(id).Increment());
        await runtime.Restart(ct);
        Assert.Equal(1, await runtime.Get<ICounter>(id).Count());
        Assert.Equal(2, await runtime.Get<ICounter>(id).Increment());
    }
}
