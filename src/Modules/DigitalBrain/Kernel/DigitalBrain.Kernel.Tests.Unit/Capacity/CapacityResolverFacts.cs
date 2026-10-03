using DigitalBrain.Platform.Capacity;
using DigitalBrain.Sdk.Capacity;

namespace DigitalBrain.Kernel.Tests.Unit.Capacity;

public sealed class CapacityResolverFacts
{
    private static readonly CapacityScope Scope = new("brain-1", "app-1");

    private sealed record Source(string Kind, string Origin) : ICapacityConfiguredSource;

    private sealed class Provisioner(string kind, string origin) : ICapacityProvisioner
    {
        public int Calls;
        public string Kind => kind;
        public ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct) { Calls++; return ValueTask.FromResult(origin); }
    }

    [Fact]
    public async Task AConfiguredSourceAnswersBeforeAnyProvisioner()
    {
        var provisioner = new Provisioner("postgres", "db:provisioned");
        var resolver = new CapacityResolver([new Source("postgres", "platform")], [provisioner]);
        var resolved = await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken);
        Assert.Equal(new ResolvedCapacity("postgres", "platform"), resolved);
        Assert.Equal(0, provisioner.Calls);
    }

    [Fact]
    public async Task WithoutAConfiguredSourceTheProvisionerForTheKindAnswers()
    {
        var resolver = new CapacityResolver([new Source("clickhouse", "platform")], [new Provisioner("postgres", "db:brain-1")]);
        var resolved = await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken);
        Assert.Equal("db:brain-1", resolved.Origin);
    }

    [Fact]
    public async Task WithNothingRegisteredResolutionRefusesWithTheExactMessage()
    {
        var resolver = new CapacityResolver([], []);
        var refusal = await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await resolver.Resolve("postgres", Scope, TestContext.Current.CancellationToken));
        Assert.Equal("Sorry, runtime provisioning is not accessible at the moment.", refusal.Message);
    }

    [Fact]
    public async Task AnExplicitProvisionRequiresAProvisionerEvenWhenASourceIsConfigured()
    {
        var resolver = new CapacityResolver([new Source("postgres", "platform")], []);
        await Assert.ThrowsAsync<CapacityUnavailableException>(
            async () => await resolver.Provision("postgres", Scope, TestContext.Current.CancellationToken));
    }
}
