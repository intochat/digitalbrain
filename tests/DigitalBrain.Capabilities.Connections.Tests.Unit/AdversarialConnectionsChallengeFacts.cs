namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Catalog;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Capabilities.Connections.Sealing;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using Xunit;

public sealed class AdversarialConnectionsChallengeFacts
{
    // =========================================================================
    // Challenge 1: Failed Save Isolation Stress
    // =========================================================================

    [Fact]
    public async Task FailedSaveLeavesNeuronStateCompletelyUntouchedAcrossAllOperations()
    {
        var services = new TestServices(
            new FakeSecretSealer(),
            [new ConnectorDefinition("stress", "Stress Connector", ["api-key", "oauth"], ["api-key", "oauth"])]);
        await using var os = TestOS.Local(options => options.Services = services);
        os.Register<ConnectionCatalogNeuron>();
        os.Register<FaultInjectableConnectionNeuron>();

        var key = ConnectionKeys.For("b-stress", "stress", "conn-1");
        var connection = os.Get<IConnection>(ConnectionKeys.IdFor(key));
        var neuronDirect = os.Get<IFaultInjectableInspection>(ConnectionKeys.IdFor(key));

        using var _ = ConnectionNeuron<StressConnectionState>.StampCaller(new CapabilityCaller("caller-1", "caller-1", "b-stress"));

        // Scenario 1: Initial Connect fails on save -> remains Disconnected with 0 secrets
        FaultInjectableConnectionNeuron.FailRecord = true;
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Connect("api-key", "CANARY_PLAIN_SECRET_1"));
        Assert.Equal("Simulated record failure", ex1.Message);

        var read1 = await connection.Read();
        Assert.Equal(ConnectionStatus.Disconnected, read1.Status);
        Assert.Equal("", read1.AuthMethodId);
        Assert.Null(read1.Account);

        // Verify neuron in-memory State itself
        Assert.Equal(ConnectionStatus.Disconnected, await neuronDirect.GetCurrentStatus());
        Assert.Equal(0, await neuronDirect.GetSecretsCount());

        // Scenario 2: Successful Connect succeeds
        FaultInjectableConnectionNeuron.FailRecord = false;
        var connected = await connection.Connect("api-key", "CANARY_PLAIN_SECRET_1");
        Assert.Equal(ConnectionStatus.Connected, connected.Status);
        Assert.Equal(1, await neuronDirect.GetSecretsCount());
        Assert.Equal(ConnectionStatus.Connected, await neuronDirect.GetCurrentStatus());

        // Scenario 3: Disconnect fails on save -> remains Connected, secrets NOT deleted
        FaultInjectableConnectionNeuron.FailRecord = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Disconnect());

        var readAfterFailedDisc = await connection.Read();
        Assert.Equal(ConnectionStatus.Connected, readAfterFailedDisc.Status);
        Assert.Equal(1, await neuronDirect.GetSecretsCount());
        Assert.Equal(ConnectionStatus.Connected, await neuronDirect.GetCurrentStatus());

        // Scenario 4: Probe outcome save failure -> status remains previous (Connected)
        FaultInjectableConnectionNeuron.ProbeOutcomeToReturn = ConnectionProbeOutcome.Expired;
        FaultInjectableConnectionNeuron.FailRecord = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Probe());

        var readAfterFailedProbe = await connection.Read();
        Assert.Equal(ConnectionStatus.Connected, readAfterFailedProbe.Status); // Still Connected, did not mutate to Expired!
        Assert.Equal(ConnectionStatus.Connected, await neuronDirect.GetCurrentStatus());

        // Scenario 5: ShareInto failure -> remains unshared
        FaultInjectableConnectionNeuron.FailRecord = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ShareInto("circle-x", "app-y"));
        Assert.False(await connection.IsSharedInto("circle-x", "app-y"));

        // Scenario 6: Successful Disconnect
        FaultInjectableConnectionNeuron.FailRecord = false;
        await connection.Disconnect();
        Assert.Equal(ConnectionStatus.Disconnected, await neuronDirect.GetCurrentStatus());
        Assert.Equal(0, await neuronDirect.GetSecretsCount());
    }

    // =========================================================================
    // Challenge 2: CQRS Projection Stress
    // =========================================================================

    [Fact]
    public async Task CQRSProjectionStressWithInterleavedAndDuplicateLifecycleSignals()
    {
        var connectors = new[]
        {
            new ConnectorDefinition("c1", "Connector 1", ["token"], []),
            new ConnectorDefinition("c2", "Connector 2", ["token"], []),
        };
        var offers = new DefaultConnectorOffers(connectors);
        await using var os = TestOS.Local(options =>
        {
            options.Services = new TestServices(new FakeSecretSealer(), connectors, offers);
        });

        os.Register<StressEmitterNeuron>();
        os.Register<ConnectionCatalogNeuron>();

        var brain = new BrainId("brain-cqrs-stress");
        var catalogNeuron = os.Get<IConnectionCatalog>(IConnectionCatalog.Id(brain.Value));
        var emitter = os.Get<IStressEmitter>(new NeuronId(brain, "stress-emitter", "main"));

        // 1. Initial empty catalog
        var cat0 = await catalogNeuron.Read();
        Assert.Empty(cat0.Rows);
        Assert.Equal(0, cat0.Revision);

        // 2. Adopted signal for c1/conn-A -> Disconnected status, metadata recorded
        await emitter.Adopted("c1", "conn-A", "token", "acc-A@example.com");
        await os.Idle();

        var cat1 = await catalogNeuron.Read();
        Assert.Single(cat1.Rows);
        Assert.Equal(ConnectionStatus.Disconnected, cat1.Rows[0].Status);
        Assert.Equal("acc-A@example.com", cat1.Rows[0].Account);
        Assert.Equal(1, cat1.Revision);

        // 3. Connected signal for c1/conn-A -> Connected status
        var probedTime = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        await emitter.Connected("c1", "conn-A", "token", "acc-A@example.com", probedTime);
        await os.Idle();

        var cat2 = await catalogNeuron.Read();
        Assert.Single(cat2.Rows);
        Assert.Equal(ConnectionStatus.Connected, cat2.Rows[0].Status);
        Assert.Equal(probedTime, cat2.Rows[0].LastProbedAt);
        Assert.Equal(2, cat2.Revision);

        // 4. Failing signal with null probedAt -> status Failing, preserves account and previous probedAt
        await emitter.Failing("c1", "conn-A", "Network timeout");
        await os.Idle();

        var cat3 = await catalogNeuron.Read();
        Assert.Single(cat3.Rows);
        Assert.Equal(ConnectionStatus.Failing, cat3.Rows[0].Status);
        Assert.Equal("acc-A@example.com", cat3.Rows[0].Account); // Account preserved!
        Assert.Equal(probedTime, cat3.Rows[0].LastProbedAt); // probedAt preserved!
        Assert.Equal(3, cat3.Revision);

        // 5. Expired signal -> status Expired, preserves account
        await emitter.Expired("c1", "conn-A");
        await os.Idle();

        var cat4 = await catalogNeuron.Read();
        Assert.Equal(ConnectionStatus.Expired, cat4.Rows[0].Status);
        Assert.Equal("acc-A@example.com", cat4.Rows[0].Account);
        Assert.Equal(4, cat4.Revision);

        // 6. Connected signal again -> status Connected
        await emitter.Connected("c1", "conn-A", "token", "acc-A@example.com", probedTime);
        await os.Idle();

        var cat5 = await catalogNeuron.Read();
        Assert.Equal(ConnectionStatus.Connected, cat5.Rows[0].Status);
        Assert.Equal(5, cat5.Revision);

        // 7. Add second connection c2/conn-B
        await emitter.Connected("c2", "conn-B", "token", "acc-B@example.com", probedTime);
        await os.Idle();

        var cat6 = await catalogNeuron.Read();
        Assert.Equal(2, cat6.Rows.Length);
        Assert.Equal(6, cat6.Revision);

        // 8. Disconnect c1/conn-A -> only c2/conn-B remains
        await emitter.Disconnected("c1", "conn-A");
        await os.Idle();

        var cat7 = await catalogNeuron.Read();
        Assert.Single(cat7.Rows);
        Assert.Equal("conn-B", cat7.Rows[0].ConnectionId);
        Assert.Equal(7, cat7.Revision);

        // 9. Idempotent Disconnect for already-disconnected c1/conn-A -> NO-OP, NO revision bump!
        await emitter.Disconnected("c1", "conn-A");
        await os.Idle();

        var cat8 = await catalogNeuron.Read();
        Assert.Single(cat8.Rows);
        Assert.Equal(7, cat8.Revision); // Revision did NOT bump!
    }

    // =========================================================================
    // Challenge 3: Silent OAuth Refresh Challenge
    // =========================================================================

    [Fact]
    public async Task SilentOAuthRefreshesDoNotBumpCatalogRevisionOrEmitConnectionsChanged()
    {
        var connectors = new[] { new ConnectorDefinition("c1", "Connector 1", ["token"], []) };
        var offers = new DefaultConnectorOffers(connectors);
        await using var os = TestOS.Local(options =>
        {
            options.Services = new TestServices(new FakeSecretSealer(), connectors, offers);
        });

        os.Register<StressEmitterNeuron>();
        os.Register<ConnectionCatalogNeuron>();

        var brain = new BrainId("brain-silent-refresh");
        var catalogNeuron = os.Get<IConnectionCatalog>(IConnectionCatalog.Id(brain.Value));
        var emitter = os.Get<IStressEmitter>(new NeuronId(brain, "stress-emitter", "main"));

        // Activate catalog neuron to ensure brain-wide synapse subscription is registered
        await catalogNeuron.Subscribed();

        // Setup 1 connected connection
        var probed = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        await emitter.Connected("c1", "conn-1", "token", "user@test.org", probed);
        await os.Idle();

        var catBefore = await catalogNeuron.Read();
        var baselineRevision = catBefore.Revision;
        Assert.True(baselineRevision > 0);

        // Fire 10 silent token refresh updates (ConnectionUpdated)
        for (var i = 0; i < 10; i++)
        {
            await emitter.Updated("c1", "conn-1");
        }
        await os.Idle();

        // Catalog revision MUST remain completely unchanged!
        var catAfterUpdated = await catalogNeuron.Read();
        Assert.Equal(baselineRevision, catAfterUpdated.Revision);

        // Check journal of catalog neuron: zero ConnectionsChanged signals were emitted during refreshes
        var catalogId = IConnectionCatalog.Id(brain.Value);
        var changedSignals = os.Journal(brain).Signals(catalogId)
            .Select(e => e.Signal)
            .OfType<ConnectionsChanged>()
            .ToList();
        Assert.Single(changedSignals); // Only the initial one from ConnectionConnected!

        // Fire duplicate ConnectionConnected with identical fields
        for (var i = 0; i < 5; i++)
        {
            await emitter.Connected("c1", "conn-1", "token", "user@test.org", probed);
        }
        await os.Idle();

        var catAfterIdentical = await catalogNeuron.Read();
        Assert.Equal(baselineRevision, catAfterIdentical.Revision); // Still strictly baselineRevision!
        var changedSignalsAfter = os.Journal(brain).Signals(catalogId)
            .Select(e => e.Signal)
            .OfType<ConnectionsChanged>()
            .ToList();
        Assert.Single(changedSignalsAfter); // Still only the initial 1!
    }

    // =========================================================================
    // Challenge 5: Secret Redaction Challenge
    // =========================================================================

    [Fact]
    public void SecretRedactionGuaranteesZeroPlaintextSecretLeakageInToString()
    {
        const string canarySecretToken = "CANARY_TOKEN_998877_TOP_SECRET";
        const string canaryCipherValue = "CANARY_CIPHER_VALUE_A1B2C3D4";
        const string canaryCipherAccess = "CANARY_CIPHER_ACCESS_E5F6G7H8";
        const string canaryCipherRefresh = "CANARY_CIPHER_REFRESH_I9J0K1L2";
        const string canaryAccount = "canary_user_private@corp.internal";
        const string canaryScope = "canary:admin:super:secret:scope";
        const string canaryDetail = "canary detailed diagnostics containing auth info";

        var state = new DerivedStressConnectionState
        {
            AuthMethodId = "oauth2-canary",
            Account = canaryAccount,
            Scopes = canaryScope,
            Detail = canaryDetail,
            SharedInto = ["circle:super_canary"],
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
            {
                [ConnectionState.ValueSecret] = new() { KeyId = "k1", Cipher = canaryCipherValue },
                [ConnectionState.AccessSecret] = new() { KeyId = "k2", Cipher = canaryCipherAccess },
                [ConnectionState.RefreshSecret] = new() { KeyId = "k3", Cipher = canaryCipherRefresh },
            },
            CustomPlaintextApiKey = canarySecretToken,
        };

        var str = state.ToString();

        // Must match exact redacted format: "<TypeName> { <redacted> }"
        Assert.Equal("DerivedStressConnectionState { <redacted> }", str);

        // Adversarial string containment check against all canary secrets:
        Assert.DoesNotContain(canarySecretToken, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryCipherValue, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryCipherAccess, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryCipherRefresh, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryAccount, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryScope, str, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryDetail, str, StringComparison.Ordinal);
        Assert.DoesNotContain("circle:super_canary", str, StringComparison.Ordinal);
        Assert.DoesNotContain("oauth2-canary", str, StringComparison.Ordinal);

        // String interpolation challenge
        var interpolated = $"Current state is: {state}";
        Assert.Equal("Current state is: DerivedStressConnectionState { <redacted> }", interpolated);
        Assert.DoesNotContain(canarySecretToken, interpolated, StringComparison.Ordinal);

        // Base ConnectionState test
        var baseState = new ConnectionState
        {
            AuthMethodId = "token",
            Account = "alice@example.com",
            Secrets = new Dictionary<string, SealedSecret>(StringComparer.Ordinal)
            {
                ["val"] = new() { KeyId = "k1", Cipher = "secret-cipher" },
            },
        };
        var baseStr = baseState.ToString();
        Assert.Equal("ConnectionState { <redacted> }", baseStr);
        Assert.DoesNotContain("alice", baseStr, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-cipher", baseStr, StringComparison.Ordinal);
    }

    // =========================================================================
    // Test Harness Types
    // =========================================================================

    private sealed record StressConnectionState : ConnectionState;

    private sealed record DerivedStressConnectionState : ConnectionState
    {
        public string CustomPlaintextApiKey { get; init; } = "";
    }

    private interface IFaultInjectableInspection
    {
        Task<ConnectionStatus> GetCurrentStatus();
        Task<int> GetSecretsCount();
    }

    [Neuron("stress")]
    private sealed class FaultInjectableConnectionNeuron : ConnectionNeuron<StressConnectionState>, IFaultInjectableInspection
    {
        public static bool FailRecord { get; set; }
        public static ConnectionProbeOutcome ProbeOutcomeToReturn { get; set; } = ConnectionProbeOutcome.Connected;

        public Task<ConnectionStatus> GetCurrentStatus() => Task.FromResult(State.Status);
        public Task<int> GetSecretsCount() => Task.FromResult(State.Secrets.Count);

        protected override Task Record(StressConnectionState next, Signal fact)
        {
            if (FailRecord)
            {
                throw new InvalidOperationException("Simulated record failure");
            }

            return base.Record(next, fact);
        }

        protected override Task<ConnectionProbeResult> ProbeAsync(Credential credential, CancellationToken ct)
            => Task.FromResult(ProbeOutcomeToReturn switch
            {
                ConnectionProbeOutcome.Connected => ConnectionProbeResult.Connected(),
                ConnectionProbeOutcome.Expired => ConnectionProbeResult.Expired("Probe expired"),
                _ => ConnectionProbeResult.Failing("Probe failing"),
            });
    }

    [Neuron("stress-emitter")]
    private sealed class StressEmitterNeuron : Neuron, IStressEmitter
    {
        public Task Connected(string connectorId, string connectionId, string authMethodId, string account, DateTimeOffset? probedAt = null)
            => Publish(new ConnectionConnected(connectorId, connectionId, authMethodId, account, probedAt));

        public Task Expired(string connectorId, string connectionId, DateTimeOffset? probedAt = null)
            => Publish(new ConnectionExpired(connectorId, connectionId, probedAt));

        public Task Failing(string connectorId, string connectionId, string detail, DateTimeOffset? probedAt = null)
            => Publish(new ConnectionFailing(connectorId, connectionId, detail, probedAt));

        public Task Adopted(string connectorId, string connectionId, string authMethodId, string account)
            => Publish(new ConnectionAdopted(connectorId, connectionId, authMethodId, account));

        public Task Disconnected(string connectorId, string connectionId)
            => Publish(new ConnectionDisconnected(connectorId, connectionId));

        public Task Updated(string connectorId, string connectionId)
            => Publish(new ConnectionUpdated(connectorId, connectionId));
    }

    private interface IStressEmitter
    {
        Task Connected(string connectorId, string connectionId, string authMethodId, string account, DateTimeOffset? probedAt = null);
        Task Expired(string connectorId, string connectionId, DateTimeOffset? probedAt = null);
        Task Failing(string connectorId, string connectionId, string detail, DateTimeOffset? probedAt = null);
        Task Adopted(string connectorId, string connectionId, string authMethodId, string account);
        Task Disconnected(string connectorId, string connectionId);
        Task Updated(string connectorId, string connectionId);
    }

    private sealed class FakeSecretSealer : ISecretSealer
    {
        public SealedSecret Seal(string name, string plaintext) => new() { KeyId = "mk-test", Cipher = $"sealed:{name}:{plaintext}" };
        public string Open(SealedSecret secret) => secret.Cipher;
    }

    private sealed class TestServices : IServiceProvider
    {
        private readonly ISecretSealer _sealer;
        private readonly IReadOnlyList<ConnectorDefinition> _connectors;
        private readonly IConnectorOffers? _offers;

        public TestServices(ISecretSealer sealer, IReadOnlyList<ConnectorDefinition> connectors, IConnectorOffers? offers = null)
        {
            _sealer = sealer;
            _connectors = connectors;
            _offers = offers;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ISecretSealer)) { return _sealer; }
            if (serviceType == typeof(IReadOnlyList<ConnectorDefinition>)) { return _connectors; }
            if (serviceType == typeof(IEnumerable<ConnectorDefinition>)) { return _connectors; }
            if (serviceType == typeof(IConnectorOffers)) { return _offers; }
            return null;
        }
    }
}
