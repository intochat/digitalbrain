using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class FeatureCapabilityInvokerTests
{
    private static readonly BrainOwnerId Owner = new("owner-scope");
    private static readonly ActorId Actor = new("actor-scope");
    private static readonly DateTimeOffset OccurredAt = new(2026, 7, 15, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvokeAsync_enqueues_one_canonical_deterministic_feature_input()
    {
        var gateway = new RecordingFeatureRunGateway();
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));
        var invocation = Invocation("{\"z\":{\"b\":2,\"a\":1},\"a\":\"value\"}");

        var first = await invoker.InvokeAsync(invocation);
        gateway.Status = FeatureAppendStatus.Duplicate;
        var second = await invoker.InvokeAsync(invocation);

        Assert.Equal(FeatureCapabilityInvocationStatus.Started, first.Status);
        Assert.Equal(FeatureCapabilityInvocationStatus.Started, second.Status);
        Assert.Equal(2, gateway.Commands.Count);
        var command = gateway.Commands[0];
        Assert.Equal(Owner, command.OwnerId);
        Assert.Equal(Actor, command.ActorId);
        Assert.Equal("manual", command.Input.Kind);
        Assert.Equal("{\"a\":\"value\",\"z\":{\"a\":1,\"b\":2}}", command.Input.PayloadJson);
        Assert.Equal(OccurredAt, command.Input.OccurredAt);
        Assert.Equal(command.Input, gateway.Commands[1].Input);
        Assert.StartsWith("ino-input-", command.Input.InputId, StringComparison.Ordinal);
        Assert.StartsWith("ino-correlation-", command.Input.CorrelationId, StringComparison.Ordinal);
        Assert.StartsWith("ino-trace-", command.Input.TraceId, StringComparison.Ordinal);
        Assert.StartsWith("ino-causation-", command.Input.CausationId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_rejects_arguments_that_cannot_cross_the_feature_sdk_boundary()
    {
        var gateway = new RecordingFeatureRunGateway();
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));
        var tooMany = "{" + string.Join(',', Enumerable.Range(0, 33).Select(index => $"\"p{index}\":{index}")) + "}";
        var longKey = "{\"" + new string('k', 129) + "\":1}";
        var longValue = "{\"value\":\"" + new string('v', 4097) + "\"}";

        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(Invocation("[]")));
        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(Invocation(tooMany)));
        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(Invocation(longKey)));
        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(Invocation(longValue)));
        Assert.Empty(gateway.Commands);
    }

    [Fact]
    public async Task InvokeAsync_fails_closed_when_a_required_connection_is_no_longer_healthy()
    {
        var gateway = new RecordingFeatureRunGateway();
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth());

        var result = await invoker.InvokeAsync(Invocation("{}"));

        Assert.Equal(FeatureCapabilityInvocationStatus.Unavailable, result.Status);
        Assert.Empty(gateway.Commands);
    }

    [Fact]
    public async Task InvokeAsync_fails_closed_when_the_exact_connection_instance_is_not_healthy()
    {
        var gateway = new RecordingFeatureRunGateway();
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));
        var invocation = Invocation("{}") with
        {
            Binding = Invocation("{}").Binding with
            {
                RequiredConnections =
                [
                    new CapabilityConnectionBinding(
                        "google",
                        new ProviderConnectionId("google-primary"))
                ]
            }
        };

        var result = await invoker.InvokeAsync(invocation);

        Assert.Equal(FeatureCapabilityInvocationStatus.Unavailable, result.Status);
        Assert.Empty(gateway.Commands);
    }

    [Theory]
    [InlineData(FeatureAppendStatus.Full, FeatureCapabilityInvocationStatus.Unavailable)]
    [InlineData(FeatureAppendStatus.Paused, FeatureCapabilityInvocationStatus.Unavailable)]
    public async Task InvokeAsync_never_reports_a_rejected_enqueue_as_started(
        FeatureAppendStatus appendStatus,
        FeatureCapabilityInvocationStatus expected)
    {
        var gateway = new RecordingFeatureRunGateway { Status = appendStatus };
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));

        var result = await invoker.InvokeAsync(Invocation("{}"));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_maps_a_stale_authority_rejection_to_unavailable()
    {
        var gateway = new RecordingFeatureRunGateway
        {
            Exception = new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition)
        };
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));

        var result = await invoker.InvokeAsync(Invocation("{}"));

        Assert.Equal(FeatureCapabilityInvocationStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_surfaces_an_input_identity_conflict_as_outcome_unknown()
    {
        var gateway = new RecordingFeatureRunGateway
        {
            Exception = new FeatureCommandRejectedException(FeatureCommandRejectionReason.Conflict)
        };
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));

        await Assert.ThrowsAsync<FeatureCapabilityOutcomeUnknownException>(() =>
            invoker.InvokeAsync(Invocation("{}")));
    }

    [Fact]
    public async Task InvokeAsync_surfaces_an_unconfirmed_gateway_result_as_outcome_unknown()
    {
        var gateway = new RecordingFeatureRunGateway
        {
            Exception = new InvalidOperationException("transport result unavailable")
        };
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));

        await Assert.ThrowsAsync<FeatureCapabilityOutcomeUnknownException>(() =>
            invoker.InvokeAsync(Invocation("{}")));
    }

    [Fact]
    public async Task InvokeAsync_rejects_a_binding_for_another_actor_before_enqueue()
    {
        var gateway = new RecordingFeatureRunGateway();
        var invoker = new FeatureCapabilityInvoker(gateway, new StaticOwnerConnectionHealth("google"));
        var invocation = Invocation("{}") with { ActorId = new ActorId("another-actor") };

        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(invocation));
        Assert.Empty(gateway.Commands);
    }

    private static FeatureCapabilityInvocation Invocation(string argumentsJson)
    {
        var installationId = new FeatureInstallationId("inbox-brief");
        var descriptor = new CapabilityDescriptor(
            OwnerCapabilityCatalog.FeatureDescriptorId(installationId),
            1,
            "Inbox brief",
            "Summarize the selected inbox",
            [],
            [],
            ["google"],
            CapabilityOrigin.Feature,
            CapabilityOperationKind.InternalWrite,
            true);
        var binding = new FeatureCapabilityBinding(
            Owner,
            Actor,
            installationId,
            new ReleaseDigest(new string('a', 64)),
            new GrantRevision(4),
            "manual",
            7,
            "authority-digest",
            "access-digest",
            [new CapabilityConnectionBinding("google", new ProviderConnectionId("google"))]);
        return new FeatureCapabilityInvocation(
            descriptor,
            binding,
            Owner,
            Actor,
            "operation-1",
            "conversation-1",
            "request-1",
            OccurredAt,
            new RetainedInoCapabilityPayload(descriptor.Id, JsonElement.Parse(argumentsJson)));
    }

    private sealed class RecordingFeatureRunGateway : IFeatureRunGateway
    {
        public List<StartFeatureRun> Commands { get; } = [];
        public FeatureAppendStatus Status { get; set; } = FeatureAppendStatus.Accepted;
        public Exception? Exception { get; init; }

        public Task<FeatureAppendStatus> StartAsync(StartFeatureRun command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Exception is null
                ? Task.FromResult(Status)
                : Task.FromException<FeatureAppendStatus>(Exception);
        }
    }

    private sealed class StaticOwnerConnectionHealth(params string[] healthy) : IOwnerConnectionHealth
    {
        public Task<IReadOnlySet<CapabilityConnectionBinding>> ReadHealthyAsync(
            BrainOwnerId ownerId,
            IReadOnlyCollection<CapabilityConnectionBinding> connections,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<CapabilityConnectionBinding>>(connections
                .Where(connection => healthy.Contains(connection.Provider, StringComparer.Ordinal))
                .Where(static connection => connection.ConnectionId is null ||
                    string.Equals(connection.ConnectionId.Value.Value, connection.Provider, StringComparison.Ordinal))
                .ToHashSet());
    }
}
