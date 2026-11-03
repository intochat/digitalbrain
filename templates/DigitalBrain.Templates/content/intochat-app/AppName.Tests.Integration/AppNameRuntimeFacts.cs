using DigitalBrain;
using DigitalBrain.Identity.Local;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace APP_NAMESPACE.Tests.Integration;

public sealed class APP_CLASSRuntimeFixture : BothRuntimesFixture
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-app-" + Guid.NewGuid().ToString("N"));

    protected override IReadOnlyList<Type> Modules => [typeof(APP_CLASS)];

    protected override void ConfigureSilo(IServiceCollection services)
        => services.AddLocalIdentity(SiloNeurons.Get, _home);
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
