namespace DigitalBrain.Identity.Local.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Connections.Catalog;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Identity.Local.Connections;
using DigitalBrain.Modules;
using Xunit;

public sealed class AdversarialLocalConnectionsChallengeFacts
{
    [Fact]
    public async Task ListInOrderExecutesInExactlyOneNeuronCallToConnectionCatalogRead()
    {
        var brain = new BrainId("single-call-brain");
        var catalogKey = IConnectionCatalog.Key(brain.Value);

        // Prepare a catalog with 10 connections across 3 connectors
        var rows = new List<CatalogRow>();
        for (var i = 1; i <= 10; i++)
        {
            var connector = $"connector-{(i % 3) + 1}";
            var connId = $"conn-{i}";
            var key = ConnectionKeys.For(brain.Value, connector, connId);
            var status = i % 2 == 0 ? ConnectionStatus.Connected : ConnectionStatus.Failing;
            rows.Add(new CatalogRow(key, connector, connId, status, $"user-{i}@test.org", DateTimeOffset.UtcNow));
        }

        // Custom order placing conn-7 and conn-3 first
        var order = new[]
        {
            ConnectionKeys.For(brain.Value, "connector-2", "conn-7"),
            ConnectionKeys.For(brain.Value, "connector-1", "conn-3"),
        };

        var testCatalog = new ConnectionCatalog
        {
            Rows = [.. rows],
            Order = order,
            Revision = 42,
        };

        var catalogMock = new InstrumentedCatalog(testCatalog);
        var resolverCalls = new List<(Type Contract, string Key)>();

        object Resolver(Type type, string key)
        {
            resolverCalls.Add((type, key));
            if (type == typeof(IConnectionCatalog) && key == catalogKey)
            {
                return catalogMock;
            }

            throw new InvalidOperationException($"Unexpected neuron resolution for {type.Name} / {key}");
        }

        var types = new ConnectionNeuronTypes([typeof(TestConnectorModule)]);
        var localConnections = new LocalConnections(Resolver, types);

        // Execute ListInOrder
        var result = await localConnections.ListInOrder(brain, CancellationToken.None);

        // Verification: Exactly 1 neuron resolver call
        Assert.Single(resolverCalls);
        Assert.Equal(typeof(IConnectionCatalog), resolverCalls[0].Contract);
        Assert.Equal(catalogKey, resolverCalls[0].Key);

        // Verification: Exactly 1 call to IConnectionCatalog.Read()
        Assert.Equal(1, catalogMock.ReadCalls);
        Assert.Equal(0, catalogMock.SubscribedCalls);

        // Verification: Exactly 10 items returned, with conn-7 and conn-3 leading the list
        Assert.Equal(10, result.Count);
        Assert.Equal(order[0], result[0].Key);
        Assert.Equal("user-7@test.org", result[0].Label);
        Assert.False(result[0].Connected); // conn-7 is odd -> Failing

        Assert.Equal(order[1], result[1].Key);
        Assert.Equal("user-3@test.org", result[1].Label);
        Assert.False(result[1].Connected); // conn-3 is odd -> Failing

        // Remaining 8 items follow
        var remaining = result.Skip(2).Select(r => r.Key).ToList();
        Assert.DoesNotContain(order[0], remaining);
        Assert.DoesNotContain(order[1], remaining);
    }

    [Fact]
    public async Task BindEnforcesOwnerCheckAndSingleCatalogReadBeforeNeuronLookup()
    {
        var brain = new BrainId("bind-check-brain");
        var catalogKey = IConnectionCatalog.Key(brain.Value);
        var validKey = ConnectionKeys.For(brain.Value, "c1", "conn-1");
        var foreignKey = ConnectionKeys.For("other-brain", "c1", "conn-1");
        var untrackedKey = ConnectionKeys.For(brain.Value, "c1", "untracked");

        var testCatalog = new ConnectionCatalog
        {
            Rows = [new CatalogRow(validKey, "c1", "conn-1", ConnectionStatus.Connected, "alice@test.org", null)],
            Revision = 1,
        };

        var catalogMock = new InstrumentedCatalog(testCatalog);
        var resolverCalls = new List<(Type Contract, string Key)>();
        var boundInstance = new DummyCapability();

        object Resolver(Type type, string key)
        {
            resolverCalls.Add((type, key));
            if (type == typeof(IConnectionCatalog) && key == catalogKey)
            {
                return catalogMock;
            }

            if (type == typeof(IDummyCapability) && key == validKey)
            {
                return boundInstance;
            }

            throw new KeyNotFoundException($"Key '{key}' not found for {type.Name}");
        }

        var localConnections = new LocalConnections(Resolver, null);

        // 1. Foreign brain key throws immediately WITHOUT calling catalog
        resolverCalls.Clear();
        var exForeign = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => localConnections.Bind<IDummyCapability>(brain, foreignKey, "app1", CancellationToken.None));
        Assert.Contains("does not belong to brain", exForeign.Message, StringComparison.Ordinal);
        Assert.Empty(resolverCalls); // ZERO resolver calls!

        // 2. Untracked key queries catalog once and fails WITHOUT resolving target neuron
        resolverCalls.Clear();
        catalogMock.Reset();
        var exUntracked = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => localConnections.Bind<IDummyCapability>(brain, untrackedKey, "app1", CancellationToken.None));
        Assert.Contains("is not tracked in brain", exUntracked.Message, StringComparison.Ordinal);
        Assert.Single(resolverCalls); // Only catalog!
        Assert.Equal(typeof(IConnectionCatalog), resolverCalls[0].Contract);

        // 3. Valid key resolves catalog once, then resolves target neuron once
        resolverCalls.Clear();
        catalogMock.Reset();
        var bound = await localConnections.Bind<IDummyCapability>(brain, validKey, "app1", CancellationToken.None);
        Assert.Same(boundInstance, bound);
        Assert.Equal(2, resolverCalls.Count);
        Assert.Equal(typeof(IConnectionCatalog), resolverCalls[0].Contract);
        Assert.Equal(typeof(IDummyCapability), resolverCalls[1].Contract);
    }

    private interface IDummyCapability;
    private sealed class DummyCapability : IDummyCapability;

    [Module("test-connector-module")]
    private sealed class TestConnectorModule : DigitalBrain.Modules.Module
    {
        public static ModuleManifest Manifest { get; } = new([Capability.Storage.V1], [], [], [], [])
        {
            Id = "test-connector-module",
        };
    }

    private sealed class InstrumentedCatalog(ConnectionCatalog state) : IConnectionCatalog
    {
        public int ReadCalls { get; private set; }
        public int SubscribedCalls { get; private set; }

        public void Reset()
        {
            ReadCalls = 0;
            SubscribedCalls = 0;
        }

        public Task<ConnectionCatalog> Read()
        {
            ReadCalls++;
            return Task.FromResult(state);
        }

        public Task Subscribed()
        {
            SubscribedCalls++;
            return Task.CompletedTask;
        }
    }
}
