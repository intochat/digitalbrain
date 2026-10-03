using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Registry.Tests;

public sealed class DiscoveryFacts
{
    private static void Caller(string brain = "brain") => CallerContextStamper.Stamp(new()
    {
        PrincipalId = "user",
        AccountId = "user",
        BrainId = brain,
        Kind = CallerKind.Assistant,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    });

    [Fact]
    public async Task DiscoveryRequiresATrustedCaller()
    {
        RequestContext.Remove(CallerContextStamper.RequestContextKey);
        await Assert.ThrowsAsync<UntrustedCallerException>(() => new RegistryDiscoveryService([]).Discover("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoveryIsBoundedMetadataAndNeverEnumeratesResources()
    {
        Caller();
        var providers = Enumerable.Range(0, 30).Select(i => new ScopedProvider("provider" + i.ToString("D2"))).ToArray();
        var service = new RegistryDiscoveryService(providers);
        var first = await service.Discover("typpoo", TestContext.Current.CancellationToken);
        Assert.Equal(10, first.Capabilities.Length);
        Assert.Equal(10, first.NextOffset);
        Assert.All(first.Capabilities, item => { Assert.Empty(item.Tools); Assert.Null(item.ResourcesJson); });
        var second = await service.Browse("", null, first.NextOffset!.Value, 20, TestContext.Current.CancellationToken);
        Assert.Equal(20, second.Capabilities.Length);
        Assert.Null(second.NextOffset);
        Assert.Empty(first.Capabilities.Select(x => x.Id).Intersect(second.Capabilities.Select(x => x.Id)));
        Assert.All(providers, provider => Assert.Equal(0, provider.Reads));
    }

    [Fact]
    public async Task SelectionResolvesTheCurrentCallerAndProviderFailureIsSanitized()
    {
        var service = new RegistryDiscoveryService([new ScopedProvider(), new FailedProvider()]);
        foreach (var scope in new[] { "alice-brain", "bob-brain" })
        {
            Caller(scope);
            var result = await service.Select("scoped", TestContext.Current.CancellationToken);
            Assert.Equal(BrainScope.Create("user", scope).Id, result!.ResourcesJson);
            Assert.Equal(["read"], result.Tools);
        }
        var failed = await service.Browse("", "failed", 0, 10, TestContext.Current.CancellationToken);
        Assert.Empty(failed.Capabilities);
        Assert.DoesNotContain("private", Assert.Single(failed.Errors).Message);
        Assert.NotNull(await service.Select("scoped", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AppCallersCannotEnumerateHostResources()
    {
        CallerContextStamper.Stamp(new() { PrincipalId = "app", AccountId = "user", BrainId = "brain", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new RegistryDiscoveryService([]).Discover("", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheNeuronSerializesDescriptionsAndExplicitSelections()
    {
        await using var brain = await ModuleTest.Create().WithModule<RegistryModule>()
            .ConfigureSilo(silo => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                .AddSingleton<IRegistryResourceProvider>(silo.Services, new ScopedProvider()))
            .StartAsync(TestContext.Current.CancellationToken);
        Caller("round-trip");
        var registry = brain.Get<IRegistry>(IRegistry.Key);
        var result = await registry.Discover("resources", TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(result.Capabilities).ResourcesJson);
        Assert.Equal(BrainScope.CurrentId(), (await registry.Select("scoped", TestContext.Current.CancellationToken))!.ResourcesJson);
    }

    private sealed class ScopedProvider(string id = "scoped") : IRegistryResourceProvider
    {
        public string Id => id;
        public int Reads { get; private set; }
        public Task<RegistryDiscovery> Discover(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new RegistryDiscovery([new(Id, "Available", ["read"], BrainScope.CurrentId())], []));
        }
    }
    private sealed class FailedProvider : IRegistryResourceProvider
    {
        public string Id => "failed";
        public Task<RegistryDiscovery> Discover(CancellationToken ct) => throw new InvalidOperationException("private database details");
    }
}
