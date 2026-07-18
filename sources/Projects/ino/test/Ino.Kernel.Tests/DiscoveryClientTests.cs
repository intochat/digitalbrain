using Ino.Core;
using Ino.Core.Hosting;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

public class DiscoveryClientTests
{
    [Fact]
    public async Task LookupCanonical_does_not_cache_null_result_and_sees_later_registration()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Substitute.For<IDiscovery>();
        var factory = Substitute.For<IGrainFactory>();
        factory.GetGrain<IDiscovery>(0).Returns(grain);

        var target = new CanonicalTarget(typeof(float), typeof(object), DomainId.From("x"), []);
        grain.LookupCanonicalAsync(typeof(float), Arg.Any<CancellationToken>())
            .Returns((CanonicalTarget?)null, target);

        var client = new DiscoveryClient(factory);

        Assert.Null(await client.LookupCanonicalAsync(typeof(float), ct));
        Assert.Equal(target, await client.LookupCanonicalAsync(typeof(float), ct));
    }

    [Fact]
    public async Task LookupCanonical_caches_positive_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Substitute.For<IDiscovery>();
        var factory = Substitute.For<IGrainFactory>();
        factory.GetGrain<IDiscovery>(0).Returns(grain);

        var target = new CanonicalTarget(typeof(float), typeof(object), DomainId.From("x"), []);
        grain.LookupCanonicalAsync(typeof(float), Arg.Any<CancellationToken>()).Returns(target);

        var client = new DiscoveryClient(factory);

        Assert.Equal(target, await client.LookupCanonicalAsync(typeof(float), ct));
        Assert.Equal(target, await client.LookupCanonicalAsync(typeof(float), ct));

        await grain.Received(1).LookupCanonicalAsync(typeof(float), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LookupReactive_does_not_cache_empty_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Substitute.For<IDiscovery>();
        var factory = Substitute.For<IGrainFactory>();
        factory.GetGrain<IDiscovery>(0).Returns(grain);

        var populated = new IReadOnlyList<ReactiveTarget>[]
        {
            Array.Empty<ReactiveTarget>(),
            new[] { new ReactiveTarget(typeof(string), typeof(object), DomainId.From("x")) },
        };
        var call = 0;
        grain.LookupReactiveAsync(typeof(string), Arg.Any<CancellationToken>())
            .Returns(_ => populated[call++]);

        var client = new DiscoveryClient(factory);

        Assert.Empty(await client.LookupReactiveAsync(typeof(string), ct));
        Assert.Single(await client.LookupReactiveAsync(typeof(string), ct));
    }

    [Fact]
    public async Task LookupReactive_caches_non_empty_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Substitute.For<IDiscovery>();
        var factory = Substitute.For<IGrainFactory>();
        factory.GetGrain<IDiscovery>(0).Returns(grain);

        IReadOnlyList<ReactiveTarget> result = new[] { new ReactiveTarget(typeof(string), typeof(object), DomainId.From("x")) };
        grain.LookupReactiveAsync(typeof(string), Arg.Any<CancellationToken>()).Returns(result);

        var client = new DiscoveryClient(factory);

        await client.LookupReactiveAsync(typeof(string), ct);
        await client.LookupReactiveAsync(typeof(string), ct);

        await grain.Received(1).LookupReactiveAsync(typeof(string), Arg.Any<CancellationToken>());
    }
}
