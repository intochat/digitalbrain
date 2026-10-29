using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Identity;
using DigitalBrain.Testing.Conformance.Secrets;
using DigitalBrain.Testing.Conformance.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

public sealed class CapabilityBrokerFacts
{
    private static readonly BrainId Brain = new("b1");

    private static CapabilityBroker Broker(FakeConnections connections, IServiceProvider? services = null, params ICapabilityBindingInterceptor[] interceptors)
        => new CapabilityBroker(new CapabilityRegistry(), connections, services ?? new ServiceCollection().BuildServiceProvider(), interceptors).ForApp("app.files");

    [Fact]
    public async Task ResolveAll_returns_connected_storage_connections_in_settings_order()
    {
        var connections = new FakeConnections(
            new BrainConnection("B", "Bee", true, [Capability.Storage.V1], "b:"),
            new BrainConnection("A", "Ay", false, [Capability.Storage.V1], "a:"),
            new BrainConnection("S", "Sec", true, [Capability.Secrets.V1], null),
            new BrainConnection("C", "Cee", true, [Capability.Storage.V1], "c:"));

        var all = await Broker(connections).ResolveAll<IStorage>(Brain, TestContext.Current.CancellationToken);

        Assert.Equal(["B", "C"], all.Select(c => c.ConnectionKey));
        Assert.Equal(["Bee", "Cee"], all.Select(c => c.Label));
        Assert.Same(connections.Bound["B"], Assert.IsType<RecordingStorage>(all[0].Capability).Inner);
        Assert.All(connections.BindCallers, caller => Assert.Equal("app.files", caller));
        Assert.Empty(connections.Requests);
    }

    [Fact]
    public async Task Storages_equals_ResolveAll_of_storage()
    {
        var connections = new FakeConnections(
            new BrainConnection("B", "Bee", true, [Capability.Storage.V1], "b:"),
            new BrainConnection("C", "Cee", true, [Capability.Storage.V1], "c:"));
        var broker = Broker(connections);

        var storages = await broker.Storages(Brain, TestContext.Current.CancellationToken);
        var all = await broker.ResolveAll<IStorage>(Brain, TestContext.Current.CancellationToken);

        Assert.Equal(all.Select(c => c.ConnectionKey), storages.Select(c => c.ConnectionKey));
    }

    [Fact]
    public async Task ResolveFor_an_item_id_with_a_root_returns_the_owning_connection_only()
    {
        var connections = new FakeConnections(
            new BrainConnection("B", "Bee", true, [Capability.Storage.V1], "b:"),
            new BrainConnection("C", "Cee", true, [Capability.Storage.V1], "c:"));
        var broker = Broker(connections);

        var owner = await broker.ResolveFor(Brain, new ItemId("c:folder/x"), TestContext.Current.CancellationToken);
        var fallback = await broker.ResolveFor(Brain, new ItemId("z:nothing"), TestContext.Current.CancellationToken);

        Assert.Equal("C", Assert.Single(owner).ConnectionKey);
        Assert.Equal(["B", "C"], fallback.Select(c => c.ConnectionKey));
    }

    [Fact]
    public async Task No_connected_connection_raises_one_connection_request_and_returns_empty()
    {
        var connections = new FakeConnections(new BrainConnection("A", "Ay", false, [Capability.Storage.V1], "a:"));

        var all = await Broker(connections).ResolveAll<IStorage>(Brain, TestContext.Current.CancellationToken);

        Assert.Empty(all);
        var request = Assert.Single(connections.Requests);
        Assert.Equal((Brain, Capability.Storage.V1, "app.files"), request);
    }

    [Fact]
    public async Task Interceptors_wrap_every_bound_connection()
    {
        var connections = new FakeConnections(new BrainConnection("B", "Bee", true, [Capability.Storage.V1], "b:"));
        var wrapped = new InMemoryStorage("wrapped");

        var all = await Broker(connections, null, new ReplacingInterceptor(wrapped)).Storages(Brain, TestContext.Current.CancellationToken);

        Assert.Same(wrapped, Assert.IsType<RecordingStorage>(Assert.Single(all).Capability).Inner);
    }

    [Fact]
    public async Task Resolve_single_returns_the_keyed_registration()
    {
        var secrets = new InMemorySecrets();
        var services = new ServiceCollection().AddKeyedSingleton<ISecrets>(Capability.Secrets.V1.ToString(), secrets).BuildServiceProvider();

        Assert.Same(secrets, await Broker(new FakeConnections(), services).Resolve<ISecrets>(Brain, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resolve_single_with_no_registration_throws_naming_the_capability()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Broker(new FakeConnections()).Resolve<ISecrets>(Brain, TestContext.Current.CancellationToken));

        Assert.Contains("secrets@1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_single_with_two_registrations_throws()
    {
        var key = Capability.Identity.V1.ToString();
        var services = new ServiceCollection()
            .AddKeyedSingleton<IIdentity>(key, new InMemoryIdentity())
            .AddKeyedSingleton<IIdentity>(key, new InMemoryIdentity())
            .BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Broker(new FakeConnections(), services).Resolve<IIdentity>(Brain, TestContext.Current.CancellationToken));
        Assert.Contains("identity@1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_check_reports_two_keyed_registrations_of_a_single_capability()
    {
        var key = Capability.Identity.V1.ToString();
        var services = new ServiceCollection()
            .AddKeyedSingleton<IIdentity>(key, new InMemoryIdentity())
            .AddKeyedSingleton<IIdentity>(key, new InMemoryIdentity())
            .AddKeyedSingleton<ISecrets>(Capability.Secrets.V1.ToString(), new InMemorySecrets());

        var issue = Assert.Single(CapabilityBroker.SingleRegistrationIssues(new CapabilityRegistry(), services));
        Assert.Contains("identity@1", issue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_single_on_a_many_capability_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Broker(new FakeConnections()).Resolve<IStorage>(Brain, TestContext.Current.CancellationToken));
    }

    private sealed class ReplacingInterceptor(IStorage replacement) : ICapabilityBindingInterceptor
    {
        public TCapability Wrap<TCapability>(BrainId brain, BrainConnection connection, string callerApp, TCapability capability) where TCapability : class
            => replacement as TCapability ?? capability;
    }

    internal sealed class FakeConnections(params BrainConnection[] connections) : ICapabilityConnections
    {
        public Dictionary<string, object> Bound { get; } = [];
        public List<string> BindCallers { get; } = [];
        public List<(BrainId, CapabilityId, string)> Requests { get; } = [];

        public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BrainConnection>>(connections);

        public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        {
            BindCallers.Add(callerApp);
            if (!Bound.TryGetValue(key, out var bound)) { Bound[key] = bound = new InMemoryStorage(key); }
            return Task.FromResult((TCapability)bound);
        }

        public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct)
        {
            Requests.Add((brain, capability, callerApp));
            return Task.CompletedTask;
        }
    }
}
