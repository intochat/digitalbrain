using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class DiscoveryFacts
{
    [Fact]
    public async Task DiscoveryRequiresATrustedCaller()
    {
        RequestContext.Remove(CallerContextStamper.RequestContextKey);
        await Assert.ThrowsAsync<UntrustedCallerException>(() => new RegistryDiscoveryService([]).Discover("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResourcesFollowTheCallerAndProviderFailuresPreserveOtherCapabilities()
    {
        var service = new RegistryDiscoveryService([new ScopedProvider(), new FailedProvider()]);
        foreach (var scope in new[] { "alice-brain", "bob-brain" })
        {
            CallerContextStamper.Stamp(new() { PrincipalId = "user", AccountId = "user", BrainId = scope, Kind = CallerKind.Assistant, StampedBy = TrustedEdge.AuthenticatedHttp });
            var result = await service.Discover("typpoo", TestContext.Current.CancellationToken);
            Assert.Equal(BrainScope.Create("user", scope).Id, Assert.Single(result.Capabilities).ResourcesJson);
            Assert.Equal("failed", Assert.Single(result.Errors).Provider);
        }
    }

    [Fact]
    public async Task AppCallersCannotEnumerateHostResources()
    {
        CallerContextStamper.Stamp(new() { PrincipalId = "app", AccountId = "user", BrainId = "brain", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RegistryDiscoveryService([]).Discover("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheNeuronReturnsSerializedDiscoveryForTheTrustedCaller()
    {
        await using var brain = await UnitTest.Create().WithModule<RegistryModule>()
            .ConfigureSilo(silo => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                .AddSingleton<IRegistryResourceProvider, ScopedProvider>(silo.Services))
            .StartAsync(TestContext.Current.CancellationToken);
        CallerContextStamper.Stamp(new() { PrincipalId = "user", AccountId = "user", BrainId = "round-trip", Kind = CallerKind.Assistant, StampedBy = TrustedEdge.AuthenticatedHttp });
        var result = await brain.Get<IRegistry>(IRegistry.Key).Discover("resources", TestContext.Current.CancellationToken);
        Assert.Equal(BrainScope.CurrentId(), Assert.Single(result.Capabilities).ResourcesJson);
        Assert.Empty(result.Errors);
    }

    private sealed class ScopedProvider : IRegistryResourceProvider
    {
        public string Id => "scoped";
        public Task<RegistryDiscovery> Discover(CancellationToken ct) => Task.FromResult(new RegistryDiscovery([new("scoped", "Available", ["read"], BrainScope.CurrentId())], []));
    }
    private sealed class FailedProvider : IRegistryResourceProvider
    {
        public string Id => "failed";
        public Task<RegistryDiscovery> Discover(CancellationToken ct) => throw new InvalidOperationException("private database details");
    }
}
