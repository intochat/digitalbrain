using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ino.Kernel.Tests;

public class DiscoveryGrainTests
{
    [Fact]
    public async Task Registering_reactive_targets_collects_multiple_per_synapse_type()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [],
            Reactive: [
                new ReactiveRegistration(typeof(string), typeof(object), DomainId.From("x")),
                new ReactiveRegistration(typeof(string), typeof(int),    DomainId.From("y")),
            ],
            Neurons: []), ct);

        var targets = await discovery.LookupReactiveAsync(typeof(string), ct);
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public async Task Duplicate_canonical_registration_throws_DiscoveryConflictException()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("kernel"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(object), DomainId.From("x"), []) ],
            Reactive: [],
            Neurons: []), ct);

        var act = async () => await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(string), DomainId.From("y"), []) ],
            Reactive: [],
            Neurons: []), ct);

        var ex = await Assert.ThrowsAsync<DiscoveryConflictException>(act);
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public async Task Re_registering_same_silo_is_idempotent_and_does_not_throw()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        var registration = new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(object), DomainId.From("x"), []) ],
            Reactive: [ new ReactiveRegistration(typeof(string), typeof(object), DomainId.From("x")) ],
            Neurons: []);

        await discovery.RegisterAsync(registration, ct);

        var act = async () => await discovery.RegisterAsync(registration, ct);
        var ex = await Record.ExceptionAsync(act);
        Assert.Null(ex);

        var canonical = await discovery.LookupCanonicalAsync(typeof(float), ct);
        Assert.NotNull(canonical);
        Assert.Equal(typeof(object), canonical!.GrainType);

        var reactive = await discovery.LookupReactiveAsync(typeof(string), ct);
        Assert.Single(reactive);
    }

    [Fact]
    public async Task Re_registering_same_silo_replaces_previous_entries()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(object), DomainId.From("x"), []) ],
            Reactive: [ new ReactiveRegistration(typeof(string), typeof(object), DomainId.From("x")) ],
            Neurons: []), ct);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(int), DomainId.From("y"), []) ],
            Reactive: [],
            Neurons: []), ct);

        var canonical = await discovery.LookupCanonicalAsync(typeof(float), ct);
        Assert.Equal(typeof(int), canonical!.GrainType);

        var reactive = await discovery.LookupReactiveAsync(typeof(string), ct);
        Assert.Empty(reactive);
    }

    [Fact]
    public async Task Different_silo_claiming_already_registered_canonical_still_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("kernel"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(object), DomainId.From("x"), []) ],
            Reactive: [],
            Neurons: []), ct);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("kernel"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(object), DomainId.From("x"), []) ],
            Reactive: [],
            Neurons: []), ct);

        var act = async () => await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [ new CanonicalRegistration(typeof(float), typeof(string), DomainId.From("y"), []) ],
            Reactive: [],
            Neurons: []), ct);

        await Assert.ThrowsAsync<DiscoveryConflictException>(act);
    }

    [Fact]
    public async Task DumpAsync_returns_registered_entries()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("kernel"),
            Canonical: [ new CanonicalRegistration(typeof(DateTime), typeof(object), DomainId.From("z"), []) ],
            Reactive: [],
            Neurons: []), ct);

        var dump = await discovery.DumpAsync(ct);
        Assert.Contains(dump.Canonical, t => t.SynapseType == typeof(DateTime));
    }

    [Fact]
    public async Task DumpNeuronsAsync_returns_aggregate_across_registered_silos()
    {
        var ct = TestContext.Current.CancellationToken;
        var discovery = new Discovery(NullLogger<Discovery>.Instance);

        var expA = new NeuronDefinition(NeuronId.From("a.verb"), "A verb", "desc", typeof(object), ["do a"]);
        var expB = new NeuronDefinition(NeuronId.From("b.verb"), "B verb", "desc", typeof(object), ["do b"]);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("kernel"),
            Canonical: [],
            Reactive: [],
            Neurons: [expA]), ct);

        await discovery.RegisterAsync(new SiloRegistration(
            Silo: DomainId.From("domains"),
            Canonical: [],
            Reactive: [],
            Neurons: [expB]), ct);

        var all = await discovery.DumpNeuronsAsync(ct);
        Assert.Equal(
            new[] { "a.verb", "b.verb" }.OrderBy(x => x),
            all.Select(e => e.Id.Value).OrderBy(x => x));
    }
}
