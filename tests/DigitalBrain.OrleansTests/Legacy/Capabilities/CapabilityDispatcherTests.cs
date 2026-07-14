using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.Capabilities;

public sealed class CapabilityDispatcherTests
{
    private static readonly BrainOwnerId Owner = new("owner-1");
    private static readonly ActorId Actor = new("actor-1");
    private static readonly FeatureInstallationId Installation = new("installation-1");
    private static readonly ReleaseDigest Digest = new(new string('a', 64));
    private static readonly ProviderConnectionId Connection = new("connection-1");
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exact_live_grant_dispatches_to_the_registered_handler()
    {
        var grant = Grant();
        var source = new MutableGrantSource(grant);
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var dispatcher = new CapabilityDispatcher([handler], source, new FixedTimeProvider(Now));

        var result = await dispatcher.ExecuteAsync(Request());

        Assert.Equal(CapabilityOperationKind.Query, result.Kind);
        Assert.True(result.Payload.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, source.Reads);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("test.query.v1", Assert.Single(handler.LastGrant!.Constraints.GetProperty("allowedToolIds").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Digest_connection_and_revision_must_match_the_live_grant()
    {
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var source = new MutableGrantSource(Grant());
        var dispatcher = new CapabilityDispatcher([handler], source, new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(
            Request(digest: new ReleaseDigest(new string('b', 64)))));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(
            Request(connection: new ProviderConnectionId("connection-2"))));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(
            Request(revision: new GrantRevision(2))));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Constraints_must_allow_the_exact_capability_before_handler_execution()
    {
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var dispatcher = new CapabilityDispatcher(
            [handler],
            new MutableGrantSource(Grant(allowedCapabilityId: "other.query.v1")),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request()));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Payload_constraints_allow_only_the_approved_recursive_subset()
    {
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var constraints = JsonSerializer.SerializeToElement(new
        {
            allowedToolIds = new[] { "test.query.v1" },
            payload = new
            {
                record = new { objectName = new[] { "Account" }, recordId = new[] { "001" } },
                fields = new[] { "Name", "Industry" }
            }
        });
        var dispatcher = new CapabilityDispatcher(
            [handler],
            new MutableGrantSource(Grant(constraints: constraints)),
            new FixedTimeProvider(Now));

        await dispatcher.ExecuteAsync(Request(payload: JsonSerializer.SerializeToElement(new
        {
            record = new { objectName = "Account", recordId = "001" },
            fields = new[] { "Name" }
        })));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request(payload: JsonSerializer.SerializeToElement(new
        {
            record = new { objectName = "Contact", recordId = "001" },
            fields = new[] { "Name" }
        }))));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request(payload: JsonSerializer.SerializeToElement(new
        {
            record = new { objectName = "Account", recordId = "001" },
            fields = new[] { "SecretField" }
        }))));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Pause_and_revocation_take_effect_on_the_next_call()
    {
        var source = new MutableGrantSource(Grant());
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var dispatcher = new CapabilityDispatcher([handler], source, new FixedTimeProvider(Now));

        await dispatcher.ExecuteAsync(Request());
        source.Current = Grant(paused: true);
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request()));
        source.Current = Grant(enabled: false, paused: false);
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request()));

        Assert.Equal(3, source.Reads);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_grant_and_unregistered_capability_fail_closed()
    {
        var source = new MutableGrantSource(null);
        var dispatcher = new CapabilityDispatcher(
            [new RecordingHandler("test.query.v1", CapabilityOperationKind.Query)],
            source,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request()));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request(capabilityId: "missing.v1")));
    }

    [Fact]
    public async Task Expired_and_excessive_deadlines_fail_before_handler_execution()
    {
        var handler = new RecordingHandler("test.query.v1", CapabilityOperationKind.Query);
        var dispatcher = new CapabilityDispatcher(
            [handler],
            new MutableGrantSource(Grant()),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request(deadline: Now)));
        await Assert.ThrowsAsync<CapabilityDeniedException>(() => dispatcher.ExecuteAsync(Request(deadline: Now.AddMinutes(2))));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Deadline_is_enforced_when_a_handler_ignores_cancellation()
    {
        var dispatcher = new CapabilityDispatcher(
            [new UncooperativeHandler()],
            new MutableGrantSource(Grant()),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.ExecuteAsync(
            Request(deadline: Now.AddMilliseconds(20))));
    }

    [Fact]
    public void Capability_request_rejects_an_oversize_payload()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { value = new string('x', CapabilityRequest.MaximumPayloadBytes) }));

        Assert.Throws<ArgumentException>(() => Request(payload: document.RootElement));
    }

    [Fact]
    public void Duplicate_handler_registration_fails_startup()
    {
        var source = new MutableGrantSource(Grant());

        Assert.Throws<InvalidOperationException>(() => new CapabilityDispatcher(
            [
                new RecordingHandler("test.query.v1", CapabilityOperationKind.Query),
                new RecordingHandler("test.query.v1", CapabilityOperationKind.ExternalEffect)
            ],
            source,
            new FixedTimeProvider(Now)));
    }

    [Fact]
    public void Hosted_startup_validation_eagerly_constructs_the_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICapabilityHandler>(new RecordingHandler("test.query.v1", CapabilityOperationKind.Query));
        services.AddSingleton<ICapabilityHandler>(new RecordingHandler("test.query.v1", CapabilityOperationKind.Query));
        services.AddSingleton<ICapabilityGrantSource>(new MutableGrantSource(Grant()));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<CapabilityGrantValidator>();
        services.AddSingleton<ICapabilityDispatcher, CapabilityDispatcher>();
        services.AddHostedService<CapabilityDispatcherStartupValidation>();
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetServices<IHostedService>().ToArray());
    }

    [Fact]
    public async Task External_effect_handlers_return_proposals_without_applying_effects()
    {
        var handler = new RecordingHandler("test.effect.v1", CapabilityOperationKind.ExternalEffect);
        var dispatcher = new CapabilityDispatcher(
            [handler],
            new MutableGrantSource(Grant(capabilityId: "test.effect.v1")),
            new FixedTimeProvider(Now));

        var result = await dispatcher.ExecuteAsync(Request(capabilityId: "test.effect.v1"));

        Assert.Equal(CapabilityOperationKind.ExternalEffect, result.Kind);
        Assert.True(result.Payload.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, handler.Calls);
    }

    private static CapabilityRequest Request(
        ReleaseDigest? digest = null,
        ProviderConnectionId? connection = null,
        GrantRevision? revision = null,
        DateTimeOffset? deadline = null,
        string capabilityId = "test.query.v1",
        JsonElement? payload = null) =>
        new(
            Owner,
            Actor,
            Installation,
            digest ?? Digest,
            "input-1",
            "operation-1",
            capabilityId,
            1,
            connection ?? Connection,
            revision ?? new GrantRevision(1),
            payload ?? JsonSerializer.SerializeToElement(new { value = "request" }),
            deadline ?? Now.AddSeconds(30),
            "correlation-1",
            null);

    private static CapabilityGrant Grant(
        bool enabled = true,
        bool paused = false,
        string capabilityId = "test.query.v1",
        string? allowedCapabilityId = null,
        JsonElement? constraints = null) =>
        new(
            Owner,
            Installation,
            Digest,
            capabilityId,
            1,
            Connection,
            new GrantRevision(1),
            constraints ?? JsonSerializer.SerializeToElement(new { allowedToolIds = new[] { allowedCapabilityId ?? capabilityId } }),
            enabled,
            paused);

    private sealed class MutableGrantSource(CapabilityGrant? current) : ICapabilityGrantSource
    {
        public CapabilityGrant? Current { get; set; } = current;
        public int Reads { get; private set; }

        public ValueTask<CapabilityGrant?> ReadAsync(
            CapabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Current);
        }
    }

    private sealed class RecordingHandler(
        string capabilityId,
        CapabilityOperationKind operationKind) : ICapabilityHandler
    {
        public string CapabilityId { get; } = capabilityId;
        public int CapabilityVersion => 1;
        public CapabilityOperationKind OperationKind { get; } = operationKind;
        public int Calls { get; private set; }
        public CapabilityGrant? LastGrant { get; private set; }

        public Task<JsonElement> ExecuteAsync(
            CapabilityRequest request,
            CapabilityGrant grant,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastGrant = grant;
            return Task.FromResult(JsonSerializer.SerializeToElement(new { accepted = true }));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class UncooperativeHandler : ICapabilityHandler
    {
        public string CapabilityId => "test.query.v1";
        public int CapabilityVersion => 1;
        public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;

        public async Task<JsonElement> ExecuteAsync(
            CapabilityRequest request,
            CapabilityGrant grant,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            return JsonSerializer.SerializeToElement(new { accepted = true });
        }
    }
}
