extern alias McpProject;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Tests.TestSupport;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using BootstrapSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.BootstrapSessionRequest;
using FeedAudienceKind = McpProject::DigitalBrain.V2.Ui.Grpc.FeedAudienceKind;
using RefreshSessionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.RefreshSessionRequest;
using SubmitActionRequest = McpProject::DigitalBrain.V2.Ui.Grpc.SubmitActionRequest;
using SurfaceFeedEvent = McpProject::DigitalBrain.V2.Ui.Grpc.SurfaceFeedEvent;
using WatchSurfaceFeedRequest = McpProject::DigitalBrain.V2.Ui.Grpc.WatchSurfaceFeedRequest;
using UiBootstrapAuthenticator = McpProject::DigitalBrain.Mcp.UiBootstrapAuthenticator;
using UiBootstrapOptions = McpProject::DigitalBrain.Mcp.UiBootstrapOptions;
using UiDeliveryOptions = McpProject::DigitalBrain.Mcp.UiDeliveryOptions;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;
using UiGrpcClient = McpProject::DigitalBrain.V2.Ui.Grpc.DigitalBrainV2Ui.DigitalBrainV2UiClient;
using UiHostingExtensions = McpProject::DigitalBrain.Mcp.UiHostingExtensions;
using UiIntegrityKeyProvider = McpProject::DigitalBrain.Mcp.UiFeedIntegrityKeyProvider;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiTransportTests
{
    private const string PromptInputJson = "{\"prompt\":\"What can you help me with in this workspace?\"}";
    private static readonly string[] Capabilities =
        ["ui.protocol.v2", "ui.payload.widgetTree", "ui.widget-vocabulary.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions"];

    [Fact]
    public void Signed_sessions_preserve_grants_and_require_the_exact_transport_audience()
    {
        var tokens = new SessionTokenService(RandomNumberGenerator.GetBytes(32));
        var context = Context("tenant", "workspace", "user");
        var ui = tokens.Issue(context, TimeSpan.FromMinutes(5), SessionAudiences.Ui);
        var mcp = tokens.Issue(context, TimeSpan.FromMinutes(5), SessionAudiences.Mcp);

        Assert.True(tokens.TryValidate(ui, SessionAudiences.Ui, out var restored));
        Assert.Contains("ui.action", restored.Grants);
        Assert.False(tokens.TryValidate(ui, SessionAudiences.Mcp, out _));
        Assert.False(tokens.TryValidate(mcp, SessionAudiences.Ui, out _));
        Assert.False(tokens.TryValidate("malformed", SessionAudiences.Ui, out _));
        Assert.False(GrpcAuthentication.TryAuthenticate(new Dictionary<string, string>(), tokens, SessionAudiences.Ui, out _));

        var expired = tokens.Issue(context, TimeSpan.FromMilliseconds(1), SessionAudiences.Ui);
        Assert.False(tokens.TryValidate(expired, SessionAudiences.Ui, out _));
        Assert.Equal(SessionAudiences.Mcp, SessionAudiences.RequireFixedMcp(null));
        Assert.Equal(SessionAudiences.Mcp, SessionAudiences.RequireFixedMcp(SessionAudiences.Mcp));
        Assert.Throws<InvalidOperationException>(() => SessionAudiences.RequireFixedMcp(string.Empty));
        Assert.Throws<InvalidOperationException>(() => SessionAudiences.RequireFixedMcp(SessionAudiences.Ui));
    }

    [Fact]
    public void Feed_sequences_and_visibility_are_isolated_by_tenant_workspace_and_derived_audience()
    {
        var store = new PrivateFeedStore();
        var actions = new ActionExecutor(store);
        var producer = new WorkspaceSurfaceProducer(store, actions);
        var first = Context("tenant", "workspace-a", "principal-a");
        var sameWorkspace = Context("tenant", "workspace-a", "principal-b");
        var otherWorkspace = Context("tenant", "workspace-b", "principal-a");
        var otherTenant = Context("tenant-b", "workspace-a", "principal-a");

        var principal = producer.EnsureInitial(first, SurfaceAudienceKind.Principal);
        var workspace = producer.EnsureInitial(first, SurfaceAudienceKind.Workspace);
        var publiclyAddressed = producer.EnsureInitial(first, SurfaceAudienceKind.Public);

        Assert.Equal(1, principal.Sequence);
        Assert.Equal(1, workspace.Sequence);
        Assert.Equal(1, publiclyAddressed.Sequence);
        Assert.Empty(store.CatchUp(sameWorkspace, SurfaceAudienceKind.Principal, 0).Items);
        Assert.Single(store.CatchUp(sameWorkspace, SurfaceAudienceKind.Workspace, 0).Items);
        Assert.Single(store.CatchUp(sameWorkspace, SurfaceAudienceKind.Public, 0).Items);
        Assert.Empty(store.CatchUp(otherWorkspace, SurfaceAudienceKind.Workspace, 0).Items);
        Assert.Empty(store.CatchUp(otherWorkspace, SurfaceAudienceKind.Public, 0).Items);
        Assert.Empty(store.CatchUp(otherTenant, SurfaceAudienceKind.Principal, 0).Items);
        Assert.Empty(store.CatchUp(otherTenant, SurfaceAudienceKind.Workspace, 0).Items);
        Assert.Empty(store.CatchUp(otherTenant, SurfaceAudienceKind.Public, 0).Items);
        Assert.Empty(workspace.Actions);
        Assert.Empty(publiclyAddressed.Actions);
        var writer = new SurfaceEnvelopeWriter(actions);
        using var workspaceEnvelope = JsonDocument.Parse(writer.Write(first, workspace, Capabilities.ToHashSet(StringComparer.Ordinal)));
        Assert.Empty(workspaceEnvelope.RootElement.GetProperty("actions").EnumerateArray());
        Assert.DoesNotContain("actionBindingId", workspaceEnvelope.RootElement.GetProperty("payload").GetRawText(), StringComparison.Ordinal);
        var principalToken = ActionToken(writer.Write(first, principal, Capabilities.ToHashSet(StringComparer.Ordinal)));
        producer.Republish(first, "workspace-only", SurfaceAudienceKind.Workspace);
        Assert.Equal(WorkspaceSurfaceProducer.InoActionType,
            actions.Use(first, WorkspaceSurfaceProducer.InoBindingId, principalToken,
                principal.SurfaceId, principal.Revision, PromptInput()).ActionType);
    }

    [Fact]
    public void Same_principal_value_with_a_different_kind_has_an_isolated_feed_action_and_ack_scope()
    {
        var feed = new PrivateFeedStore();
        var actions = new ActionExecutor(feed);
        var producer = new WorkspaceSurfaceProducer(feed, actions);
        var user = Context("tenant", "workspace", "same");
        var service = user with { Principal = new PrincipalRef("same", PrincipalKind.Service) };
        var userRecord = producer.EnsureInitial(user);
        var serviceRecord = producer.EnsureInitial(service);

        Assert.Equal(1, userRecord.Sequence);
        Assert.Equal(1, serviceRecord.Sequence);
        Assert.NotEqual(userRecord.Audience.Id, serviceRecord.Audience.Id);
        Assert.Single(feed.CatchUp(user, SurfaceAudienceKind.Principal, 0).Items);
        Assert.Single(feed.CatchUp(service, SurfaceAudienceKind.Principal, 0).Items);
        Assert.Throws<UnauthorizedAccessException>(() => new SurfaceEnvelopeWriter(actions).Write(
            service, userRecord, Capabilities.ToHashSet(StringComparer.Ordinal)));

        var token = ActionToken(new SurfaceEnvelopeWriter(actions).Write(
            user, userRecord, Capabilities.ToHashSet(StringComparer.Ordinal)));
        Assert.Equal(ActionRejection.WrongOwner,
            Assert.Throws<ActionRejectedException>(() => actions.Use(service,
                WorkspaceSurfaceProducer.InoBindingId, token, userRecord.SurfaceId, userRecord.Revision,
                PromptInput())).Reason);
        feed.MarkDelivered(user, SurfaceAudienceKind.Principal, 1);
        Assert.Throws<InvalidOperationException>(() => feed.Acknowledge(service, SurfaceAudienceKind.Principal, 1));
    }

    [Fact]
    public void Initial_surface_is_renderable_tokenized_and_keeps_secrets_out_of_payload_and_storage()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ui-feed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "feed.jsonl");
            var integrityKey = RandomNumberGenerator.GetBytes(32);
            var store = new PrivateFeedStore(path, integrityKey: integrityKey);
            var actions = new ActionExecutor(store);
            var producer = new WorkspaceSurfaceProducer(store, actions);
            var context = Context("tenant", "workspace", "principal");
            var record = producer.EnsureInitial(context);
            var json = new SurfaceEnvelopeWriter(actions).Write(context, record, Capabilities.ToHashSet(StringComparer.Ordinal));
            using var document = JsonDocument.Parse(json);
            var envelope = document.RootElement;

            Assert.Equal(2, envelope.GetProperty("protocolVersion").GetInt32());
            Assert.Equal("digitalbrain.surface", envelope.GetProperty("surfaceSchema").GetString());
            Assert.Equal(UiProtocol.SurfaceSchemaVersion, envelope.GetProperty("surfaceSchemaVersion").GetInt32());
            Assert.True(envelope.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow);
            Assert.Equal("native", envelope.GetProperty("payload").GetProperty("kind").GetString());
            Assert.Equal("inoConversation", envelope.GetProperty("payload").GetProperty("nativeKind").GetString());
            Assert.Matches("^[a-f0-9]{64}$", envelope.GetProperty("contentHash").GetString()!);
            var actionToken = envelope.GetProperty("actions")[0].GetProperty("actionToken").GetString()!;
            Assert.NotEmpty(actionToken);
            Assert.Equal(WorkspaceSurfaceProducer.InoBindingId,
                envelope.GetProperty("actions")[0].GetProperty("bindingId").GetString());
            var payloadText = envelope.GetProperty("payload").GetRawText();
            Assert.DoesNotContain("actionToken", payloadText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("accessToken", payloadText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tenant", payloadText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(actionToken, File.ReadAllText(path), StringComparison.Ordinal);

            var reopened = new PrivateFeedStore(path, integrityKey: integrityKey);
            var durable = Assert.Single(reopened.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items);
            Assert.Equal(UiProtocol.ProtocolVersion, durable.ProtocolVersion);
            Assert.Equal(UiProtocol.SurfaceSchema, durable.SurfaceSchema);
            Assert.Equal(UiProtocol.SurfaceSchemaVersion, durable.SurfaceSchemaVersion);
            Assert.Equal(UiProtocol.ActionSchemaVersion, Assert.Single(durable.Actions).ActionSchemaVersion);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Durable_feed_mutates_only_after_persistence_and_fails_closed_on_tampering()
    {
        var fail = true;
        var writes = 0;
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { kind = "native", nativeKind = "test", data = new { status = "ready" } });
        var hash = SurfaceContentHash.Compute(payload, []);
        var store = new PrivateFeedStore(appendLine: _ =>
        {
            if (fail) throw new IOException("injected durable write failure");
            writes++;
        });

        Assert.Throws<IOException>(() => store.Append(context, SurfaceAudienceKind.Principal, "surface", 1, hash,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "one", [], payload, []));
        Assert.Throws<IOException>(() => new WorkspaceSurfaceProducer(store, new ActionExecutor(store)).EnsureInitial(context));
        Assert.Empty(store.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items);
        fail = false;
        Assert.Equal(1, store.Append(context, SurfaceAudienceKind.Principal, "surface", 1, hash,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "one", [], payload, []).Sequence);
        Assert.Equal(2, store.Append(context, SurfaceAudienceKind.Principal, "surface", 2, hash,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "two", [], payload, []).Sequence);
        fail = true;
        Assert.Throws<IOException>(() => store.RetainFrom(context, SurfaceAudienceKind.Principal, 2));
        Assert.Equal(2, store.CatchUp(context, SurfaceAudienceKind.Principal, 0).Items.Count);
        Assert.Equal(2, writes);

        var root = Path.Combine(Path.GetTempPath(), "v2-ui-feed-tamper-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "feed.jsonl");
            var integrityKey = RandomNumberGenerator.GetBytes(32);
            var durable = new PrivateFeedStore(path, integrityKey: integrityKey);
            durable.Append(context, SurfaceAudienceKind.Principal, "surface", 1, hash, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "one", [], payload, []);
            var tampered = JsonNode.Parse(File.ReadAllText(path).Trim())!.AsObject();
            tampered["Record"]!["CauseId"] = "relocated-private-metadata";
            File.WriteAllText(path, tampered.ToJsonString());
            Assert.Throws<InvalidDataException>(() => new PrivateFeedStore(path, integrityKey: integrityKey));
            var quarantine = File.ReadAllText(path + ".quarantine");
            Assert.DoesNotContain("relocated-private-metadata", quarantine, StringComparison.Ordinal);
            Assert.Contains("sha256", quarantine, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Durable_feed_rejects_valid_json_with_an_incomplete_action_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ui-feed-incomplete-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "feed.jsonl");
            var store = new PrivateFeedStore(path);
            var context = Context("tenant", "workspace", "principal");
            _ = new WorkspaceSurfaceProducer(store, new ActionExecutor(store)).EnsureInitial(context);
            var node = JsonNode.Parse(File.ReadAllText(path).Trim())!.AsObject();
            node["Record"]!["Actions"]![0] = null;
            File.WriteAllText(path, node.ToJsonString());

            Assert.Throws<InvalidDataException>(() => new PrivateFeedStore(path));
            Assert.True(File.Exists(path + ".quarantine"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Feed_supports_resume_reset_snapshot_reconnect_ack_and_cancellation_without_drops()
    {
        var store = new PrivateFeedStore();
        var actions = new ActionExecutor(store);
        var producer = new WorkspaceSurfaceProducer(store, actions);
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { kind = "native", nativeKind = "test", data = new { } });
        var hash = SurfaceContentHash.Compute(payload, []);
        store.Append(context, SurfaceAudienceKind.Principal, "surface", 1, hash, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "correlation", "surface", "one", [], payload, []);
        store.Append(context, SurfaceAudienceKind.Principal, "surface", 2, hash, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "correlation", "surface", "two", [], payload, []);
        store.Append(context, SurfaceAudienceKind.Principal, "surface", 3, hash, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "correlation", "surface", "three", [], payload, []);

        var resumed = store.CatchUp(context, SurfaceAudienceKind.Principal, 1, 1);
        Assert.Equal(2, Assert.Single(resumed.Items).Sequence);
        Assert.NotNull(resumed.Next);
        var reconnected = store.CatchUp(context, SurfaceAudienceKind.Principal, 2, 10);
        Assert.Equal(3, Assert.Single(reconnected.Items).Sequence);
        Assert.Empty(store.CatchUp(context, SurfaceAudienceKind.Principal, 3, 10).Items);

        store.MarkDelivered(context, SurfaceAudienceKind.Principal, 3);
        store.Acknowledge(context, SurfaceAudienceKind.Principal, 3);
        Assert.Equal(3, store.Acknowledged(context, SurfaceAudienceKind.Principal));
        var otherSession = context with { SessionId = "another-session" };
        Assert.Throws<InvalidOperationException>(() => store.Acknowledge(otherSession, SurfaceAudienceKind.Principal, 3));
        Assert.Throws<InvalidOperationException>(() => store.Acknowledge(context, SurfaceAudienceKind.Principal, 4));
        store.RetainFrom(context, SurfaceAudienceKind.Principal, 2);
        var reset = store.CatchUp(context, SurfaceAudienceKind.Principal, 0, 10);
        Assert.True(reset.ResetRequired);
        Assert.True(reset.IsSnapshot);
        Assert.Equal(3, reset.LatestSequence);
        Assert.Equal(3, Assert.Single(reset.Items).Sequence);
        var futureReset = store.CatchUp(context, SurfaceAudienceKind.Principal, 999, 10);
        Assert.True(futureReset.ResetRequired);
        Assert.Equal(3, futureReset.LatestSequence);

        using var cancellation = new CancellationTokenSource();
        var wait = store.WaitForChangeAsync(context, SurfaceAudienceKind.Principal, 3, cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);

        store.RetainFrom(context, SurfaceAudienceKind.Principal, 999);
        var afterFullRetention = store.Append(context, SurfaceAudienceKind.Principal, "surface", 4, hash,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "surface", "four", [], payload, []);
        Assert.Equal(4, afterFullRetention.Sequence);
    }

    [Fact]
    public void Retained_multi_surface_holes_reset_atomically_and_bounded_catchup_does_not_materialize_history()
    {
        var store = new PrivateFeedStore();
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { kind = "native", nativeKind = "test", data = new { } });
        var hash = SurfaceContentHash.Compute(payload, []);
        store.Append(context, SurfaceAudienceKind.Principal, "surface-a", 1, hash, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "1", [], payload, []);
        for (var revision = 1; revision <= 8; revision++)
            store.Append(context, SurfaceAudienceKind.Principal, "surface-b", revision, hash, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", revision.ToString(), [], payload, []);
        store.Append(context, SurfaceAudienceKind.Principal, "surface-c", 1, hash, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "10", [], payload, []);
        store.RetainFrom(context, SurfaceAudienceKind.Principal, 9);

        var reset = store.CatchUp(context, SurfaceAudienceKind.Principal, 0, 1);

        Assert.True(reset.ResetRequired);
        Assert.Equal(10, reset.LatestSequence);
        Assert.Equal(new long[] { 1, 9, 10 }, reset.Items.Select(static item => item.Sequence));

        var history = new PrivateFeedStore();
        for (var revision = 1; revision <= 1_000; revision++)
            history.Append(context, SurfaceAudienceKind.Principal, "one", revision, hash, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", revision.ToString(), [], payload, []);
        var bounded = history.CatchUp(context, SurfaceAudienceKind.Principal, 0, 1);
        Assert.Single(bounded.Items);
        Assert.NotNull(bounded.Next);
    }

    [Fact]
    public void Surface_payload_policy_rejects_private_fields_recursively_before_persistence()
    {
        var persisted = 0;
        var store = new PrivateFeedStore(appendLine: _ => persisted++);
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { nested = new { workspace_id = "forbidden" } });

        Assert.Throws<ArgumentException>(() => store.Append(context, SurfaceAudienceKind.Principal, "surface", 1,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "cause",
            [], payload, []));
        Assert.Equal(0, persisted);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("tenant-id")]
    [InlineData("workspaceId")]
    [InlineData("principal")]
    [InlineData("principal_id")]
    [InlineData("userId")]
    [InlineData("grants")]
    [InlineData("accessToken")]
    [InlineData("action_token")]
    [InlineData("refreshToken")]
    [InlineData("sessionId")]
    [InlineData("secret")]
    public void Surface_payload_authority_and_credential_fields_are_rejected(string key)
    {
        var store = new PrivateFeedStore();
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["nested"] = new Dictionary<string, object?> { [key] = "private" }
        });
        Assert.Throws<ArgumentException>(() => store.Append(context, SurfaceAudienceKind.Principal, "surface", 1,
            new string('a', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "correlation", "test", "cause",
            [], payload, []));
    }

    [Fact]
    public void Reset_snapshots_are_bounded_without_advancing_past_omitted_surfaces()
    {
        var store = new PrivateFeedStore();
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { kind = "native", nativeKind = "test", data = new { } });
        Assert.True(PrivateFeedStore.MaximumActiveSurfacesPerAudience *
            (PrivateFeedStore.MaximumSurfacePayloadBytes + PrivateFeedStore.MaximumActionsPerSurface * 512) < 2 * 1024 * 1024);
        for (var index = 0; index < PrivateFeedStore.MaximumActiveSurfacesPerAudience; index++)
        {
            store.Append(context, SurfaceAudienceKind.Principal, $"surface-{index}", 1, SurfaceContentHash.Compute(payload, []),
                DateTimeOffset.UtcNow, null, "correlation", "surface", $"cause-{index}", [], payload, []);
        }

        var reset = store.CatchUp(context, SurfaceAudienceKind.Principal, 999, 1);
        Assert.True(reset.ResetRequired);
        Assert.Equal(PrivateFeedStore.MaximumActiveSurfacesPerAudience, reset.Items.Count);
        Assert.Equal(PrivateFeedStore.MaximumActiveSurfacesPerAudience, reset.LatestSequence);
        Assert.Throws<InvalidOperationException>(() => store.Append(
            context, SurfaceAudienceKind.Principal, "one-too-many", 1, SurfaceContentHash.Compute(payload, []), DateTimeOffset.UtcNow,
            null, "correlation", "surface", "overflow", [], payload, []));

        var oversized = JsonSerializer.SerializeToElement(new
        {
            kind = "native",
            nativeKind = "oversized",
            data = new { text = new string('x', PrivateFeedStore.MaximumSurfacePayloadBytes + 1) }
        });
        Assert.Throws<ArgumentException>(() => new PrivateFeedStore().Append(
            context, SurfaceAudienceKind.Principal, "oversized", 1, new string('c', 64), DateTimeOffset.UtcNow,
            null, "correlation", "surface", "oversized", [], oversized, []));
    }

    [Fact]
    public void Reconnect_renews_an_expired_stored_action_policy_with_a_new_surface_revision()
    {
        var store = new PrivateFeedStore();
        var actions = new ActionExecutor(store);
        var context = Context("tenant", "workspace", "principal");
        var payload = JsonSerializer.SerializeToElement(new { kind = "widgetTree", tree = new { Type = "text" }, data = new { } });
        StoredActionBinding[] expiredActions =
            [new(WorkspaceSurfaceProducer.InoBindingId, WorkspaceSurfaceProducer.InoActionType,
                WorkspaceSurfaceProducer.InoInputSchema, "ui.action", 1, DateTimeOffset.UtcNow.AddDays(-1))];
        store.Append(context, SurfaceAudienceKind.Principal, WorkspaceSurfaceProducer.HomeSurfaceId, 1,
            SurfaceContentHash.Compute(payload, expiredActions), DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1),
            "correlation", "surface", "bootstrap", [], payload, expiredActions);

        var renewed = new WorkspaceSurfaceProducer(store, actions).EnsureInitial(context);

        Assert.Equal(2, renewed.Revision);
        Assert.All(renewed.Actions, action => Assert.True(action.ExpiresAt > DateTimeOffset.UtcNow));
        var catchUp = store.CatchUp(context, SurfaceAudienceKind.Principal, 0);
        Assert.True(catchUp.ResetRequired);
        Assert.Equal(2, Assert.Single(catchUp.Items).Revision);
        Assert.True(renewed.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Reconnect_migrates_an_obsolete_stored_action_grant_without_losing_the_conversation()
    {
        var store = new PrivateFeedStore();
        var actions = new ActionExecutor(store);
        var context = Context("tenant", "workspace", "principal");
        var conversation = InoConversationSnapshot.Empty(context);
        var payload = WorkspaceSurfaceProducer.BuildInoPayload(conversation);
        StoredActionBinding[] obsoleteActions =
            [new(WorkspaceSurfaceProducer.InoBindingId, WorkspaceSurfaceProducer.InoActionType,
                WorkspaceSurfaceProducer.InoInputSchema, "brain.act", 1, DateTimeOffset.UtcNow.AddHours(1))];
        store.Append(context, SurfaceAudienceKind.Principal, WorkspaceSurfaceProducer.HomeSurfaceId, 1,
            SurfaceContentHash.Compute(payload, obsoleteActions), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            "correlation", "surface", "bootstrap", [], payload, obsoleteActions);

        var migrated = new WorkspaceSurfaceProducer(store, actions).EnsureInitial(context);

        Assert.Equal(2, migrated.Revision);
        Assert.Equal("ui.action", Assert.Single(migrated.Actions).RequiredGrant);
        Assert.Equal(payload.GetRawText(), migrated.Payload.GetRawText());
    }

    [Fact]
    public void Action_tokens_reauthorize_owner_workspace_revision_policy_expiry_and_replay()
    {
        var store = new PrivateFeedStore();
        var actions = new ActionExecutor(store);
        var producer = new WorkspaceSurfaceProducer(store, actions);
        var context = Context("tenant", "workspace", "principal");
        var record = producer.EnsureInitial(context);
        var writer = new SurfaceEnvelopeWriter(actions);
        var token = ActionToken(writer.Write(context, record, Capabilities.ToHashSet(StringComparer.Ordinal)));
        var input = PromptInput();

        Assert.Equal(ActionRejection.Forged,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, "forged", record.SurfaceId, record.Revision, input)).Reason);
        Assert.Equal(ActionRejection.WrongWorkspace,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context with { WorkspaceId = new("other") }, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision, input)).Reason);
        Assert.Equal(ActionRejection.WrongOwner,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context with { Principal = new("other", PrincipalKind.User) }, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision, input)).Reason);
        Assert.Equal(ActionRejection.WrongRevision,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision + 1, input)).Reason);
        Assert.Equal(ActionRejection.PolicyDenied,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context with { Grants = new HashSet<string> { "brain.read" } }, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision, input)).Reason);
        Assert.Equal(ActionRejection.PolicyDenied,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, token,
                record.SurfaceId, record.Revision, JsonSerializer.SerializeToElement(new { unexpected = true }))).Reason);

        Assert.Equal(WorkspaceSurfaceProducer.InoActionType,
            actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision, input).ActionType);
        Assert.Equal(ActionRejection.Replay,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, token, record.SurfaceId, record.Revision, input)).Reason);

        var second = producer.Republish(context, "command");
        var secondToken = ActionToken(writer.Write(context, second, Capabilities.ToHashSet(StringComparer.Ordinal)));
        producer.Republish(context, "newer-command");
        Assert.Equal(ActionRejection.WrongRevision,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, WorkspaceSurfaceProducer.InoBindingId, secondToken, second.SurfaceId, second.Revision, input)).Reason);

        var expiredBinding = record.Actions[0] with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        Assert.Equal(ActionRejection.Expired,
            Assert.Throws<ActionRejectedException>(() => actions.Issue(context, record, expiredBinding, TimeSpan.FromMinutes(1))).Reason);
    }

    [Fact]
    public async Task Executor_restart_remints_a_token_that_converges_on_the_same_durable_operation()
    {
        var feed = new PrivateFeedStore();
        var firstActions = new ActionExecutor(feed);
        var producer = new WorkspaceSurfaceProducer(feed, firstActions);
        var context = Context("tenant", "workspace", "principal");
        var record = producer.EnsureInitial(context);
        var firstToken = ActionToken(new SurfaceEnvelopeWriter(firstActions).Write(
            context, record, Capabilities.ToHashSet(StringComparer.Ordinal)));
        var firstUse = firstActions.Use(context, WorkspaceSurfaceProducer.InoBindingId, firstToken,
            record.SurfaceId, record.Revision, PromptInput());
        var commandContext = context with
        {
            IdempotencyKey = firstUse.IdempotencyKey,
            Grants = new HashSet<string> { "brain.read", "brain.act" }
        };
        var application = new ApplicationService();
        var firstOperation = await application.SubmitAsync(commandContext,
            new CommandEnvelope(firstUse.ActionType, 2, "first", commandContext, firstUse.Input));

        var restartedActions = new ActionExecutor(feed);
        var restartedToken = ActionToken(new SurfaceEnvelopeWriter(restartedActions).Write(
            context, record, Capabilities.ToHashSet(StringComparer.Ordinal)));
        var restartedUse = restartedActions.Use(context, WorkspaceSurfaceProducer.InoBindingId, restartedToken,
            record.SurfaceId, record.Revision, PromptInput());
        var restartedContext = commandContext with { IdempotencyKey = restartedUse.IdempotencyKey };
        var duplicateOperation = await application.SubmitAsync(restartedContext,
            new CommandEnvelope(restartedUse.ActionType, 2, "after-restart", restartedContext, restartedUse.Input));

        Assert.Equal(firstUse.IdempotencyKey, restartedUse.IdempotencyKey);
        Assert.Equal(firstOperation.OperationId, duplicateOperation.OperationId);
        var dispatcher = new CommandDispatcher(application, [new ProjectingInoTestHandler(producer)]);
        Assert.True(await dispatcher.DispatchAsync(firstOperation.OperationId));
        Assert.False(await dispatcher.DispatchAsync(duplicateOperation.OperationId));
        Assert.Equal(2, feed.LatestRevision(context, SurfaceAudienceKind.Principal, record.SurfaceId));
    }

    [Fact]
    public async Task An_action_token_issued_while_valid_is_rejected_after_its_wire_expiry()
    {
        var feed = new PrivateFeedStore();
        var actions = new ActionExecutor(feed);
        var producer = new WorkspaceSurfaceProducer(feed, actions);
        var context = Context("tenant", "workspace", "principal");
        var record = producer.EnsureInitial(context);
        var issued = actions.Issue(context, record, Assert.Single(record.Actions), TimeSpan.FromMilliseconds(100));

        await Task.Delay(250);

        Assert.Equal(ActionRejection.Expired,
            Assert.Throws<ActionRejectedException>(() => actions.Use(context, issued.BindingId, issued.Token,
                record.SurfaceId, record.Revision, PromptInput())).Reason);
    }

    [Fact]
    public async Task Bootstrap_and_refresh_are_scope_derived_rotating_and_audience_bound()
    {
        var fixture = CreateService();
        await Assert.ThrowsAsync<RpcException>(() => fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "wrong" }, BootstrapContext()));

        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.Equal("server-tenant", session.TenantId);
        Assert.Equal("server-workspace", session.WorkspaceId);
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        Assert.Equal(PrincipalScope.Id(authenticated.Principal), session.PrincipalId);
        Assert.DoesNotContain("brain.act", authenticated.Grants);
        Assert.Contains("ui.action", authenticated.Grants);

        var refreshed = await fixture.Service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = session.RefreshToken },
            TestServerCallContext.WithHeaders(("x-v2-audience", SessionAudiences.Ui)));
        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
        await Assert.ThrowsAsync<RpcException>(() => fixture.Service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = session.RefreshToken },
            TestServerCallContext.WithHeaders(("x-v2-audience", SessionAudiences.Ui))));
        await Assert.ThrowsAsync<RpcException>(() => fixture.Service.RefreshSession(
            new RefreshSessionRequest { RefreshToken = refreshed.RefreshToken },
            TestServerCallContext.WithHeaders(("x-v2-audience", SessionAudiences.Mcp))));
    }

    [Fact]
    public async Task Authenticated_feed_delivers_before_staying_open_and_action_submission_is_replay_safe()
    {
        var fixture = CreateService();
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        using var cancellation = new CancellationTokenSource();
        var writer = new CapturingServerStreamWriter<SurfaceFeedEvent>(cancellation.Cancel);
        var request = new WatchSurfaceFeedRequest
        {
            AfterSequence = 0,
            Audience = (FeedAudienceKind)0,
            MaxBatchSize = 10
        };
        request.ClientCapabilities.AddRange(Capabilities);
        var callContext = TestServerCallContext.WithHeaders(cancellation.Token,
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", session.AccessToken));

        await fixture.Service.WatchSurfaceFeed(request, writer, callContext);
        var surfaceEvent = Assert.Single(writer.Messages);
        Assert.Equal(SurfaceFeedEvent.EventOneofCase.SurfaceJson, surfaceEvent.EventCase);
        using var envelope = JsonDocument.Parse(surfaceEvent.SurfaceJson);
        Assert.Equal(1, envelope.RootElement.GetProperty("feedSequence").GetInt64());
        var action = envelope.RootElement.GetProperty("actions")[0];

        var actionRequest = new SubmitActionRequest
        {
            BindingId = action.GetProperty("bindingId").GetString()!,
            ActionToken = action.GetProperty("actionToken").GetString()!,
            SurfaceId = action.GetProperty("surfaceId").GetString()!,
            SurfaceRevision = action.GetProperty("surfaceRevision").GetInt32(),
            InputJson = PromptInputJson
        };
        var authContext = TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", session.AccessToken));
        var smuggledScope = actionRequest.Clone();
        smuggledScope.InputJson = "{\"nested\":{\"workspaceId\":\"other\",\"grants\":[\"brain.admin\"]}}";
        var rejectedScope = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.SubmitAction(smuggledScope, authContext));
        Assert.Equal(StatusCode.InvalidArgument, rejectedScope.StatusCode);
        var accepted = await fixture.Service.SubmitAction(actionRequest, authContext);
        Assert.StartsWith("v2-op-", accepted.OperationId);

        var dispatcher = new CommandDispatcher(fixture.Application,
            [new ProjectingInoTestHandler(fixture.Producer)]);
        Assert.True(await dispatcher.DispatchAsync(accepted.OperationId));
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        var projected = fixture.Feed.CatchUp(authenticated, SurfaceAudienceKind.Principal, 0);
        Assert.False(projected.ResetRequired);
        Assert.Equal(2, projected.Items[^1].Revision);

        var replay = await fixture.Service.SubmitAction(actionRequest, authContext);
        Assert.Equal(accepted.OperationId, replay.OperationId);
        Assert.Equal(accepted.IdempotencyKey, replay.IdempotencyKey);
        Assert.True(fixture.Actions.TryGetUse(accepted.IdempotencyKey, out var use));
        Assert.Equal(accepted.OperationId, use!.OperationId);
    }

    [Fact]
    public async Task Operation_journal_failure_does_not_consume_action_and_concurrent_retry_commits_once()
    {
        var fail = true;
        var writes = 0;
        var operationPath = Path.Combine(Path.GetTempPath(), "runtime-ui-operation-seam-" + Guid.NewGuid().ToString("N"), "operations.jsonl");
        var application = new ApplicationService(
            new AuthenticatedJournalFaultInjection(BeforePhysicalAppend: () =>
            {
                if (fail) throw new IOException("injected operation journal failure");
                Interlocked.Increment(ref writes);
            }),
            storagePath: operationPath,
            journalIntegrityKey: RandomNumberGenerator.GetBytes(32));
        var fixture = CreateService(application: application);
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        var record = fixture.Producer.EnsureInitial(authenticated);
        using var envelope = JsonDocument.Parse(new SurfaceEnvelopeWriter(fixture.Actions).Write(
            authenticated, record, Capabilities.ToHashSet(StringComparer.Ordinal)));
        var action = envelope.RootElement.GetProperty("actions")[0];
        var request = new SubmitActionRequest
        {
            BindingId = action.GetProperty("bindingId").GetString()!,
            ActionToken = action.GetProperty("actionToken").GetString()!,
            SurfaceId = record.SurfaceId,
            SurfaceRevision = record.Revision,
            InputJson = PromptInputJson
        };

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.SubmitAction(request, AuthContext(session.AccessToken)));
        Assert.Empty(application.GetPendingOperationIds());
        fail = false;
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => fixture.Service.SubmitAction(request, AuthContext(session.AccessToken))));
        Assert.Single(outcomes.Select(static reply => reply.OperationId).Distinct(StringComparer.Ordinal));
        Assert.Single(outcomes.Select(static reply => reply.IdempotencyKey).Distinct(StringComparer.Ordinal));
        Assert.Single(application.GetPendingOperationIds());
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Action_reservation_linearizes_durable_admission_before_a_concurrent_surface_revision()
    {
        using var appendEntered = new ManualResetEventSlim();
        using var allowAppend = new ManualResetEventSlim();
        var operationPath = Path.Combine(Path.GetTempPath(), "runtime-ui-linearization-" + Guid.NewGuid().ToString("N"), "operations.jsonl");
        var application = new ApplicationService(
            new AuthenticatedJournalFaultInjection(BeforePhysicalAppend: () =>
            {
                appendEntered.Set();
                Assert.True(allowAppend.Wait(TimeSpan.FromSeconds(3)));
            }),
            storagePath: operationPath,
            journalIntegrityKey: RandomNumberGenerator.GetBytes(32));
        var fixture = CreateService(application: application);
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        var record = fixture.Producer.EnsureInitial(authenticated);
        using var envelope = JsonDocument.Parse(new SurfaceEnvelopeWriter(fixture.Actions).Write(
            authenticated, record, Capabilities.ToHashSet(StringComparer.Ordinal)));
        var action = envelope.RootElement.GetProperty("actions")[0];
        var request = new SubmitActionRequest
        {
            BindingId = action.GetProperty("bindingId").GetString()!,
            ActionToken = action.GetProperty("actionToken").GetString()!,
            SurfaceId = record.SurfaceId,
            SurfaceRevision = record.Revision,
            InputJson = PromptInputJson
        };

        var submit = Task.Run(() => fixture.Service.SubmitAction(request, AuthContext(session.AccessToken)));
        Assert.True(appendEntered.Wait(TimeSpan.FromSeconds(3)));
        var publish = Task.Run(() => fixture.Producer.Republish(authenticated, "concurrent-revision"));
        Assert.NotSame(publish, await Task.WhenAny(publish, Task.Delay(100)));
        allowAppend.Set();

        var accepted = await submit;
        var refreshed = await publish;
        Assert.StartsWith("v2-op-", accepted.OperationId, StringComparison.Ordinal);
        Assert.Equal(2, refreshed.Revision);
        Assert.Single(application.GetPendingOperationIds());
    }

    [Fact]
    public async Task Open_feed_lease_expires_and_logout_revocation_blocks_later_private_delivery()
    {
        var leaseFixture = CreateService(
            new UiDeliveryOptions(TimeSpan.FromMinutes(4), TimeSpan.FromMilliseconds(25)),
            TimeSpan.FromSeconds(2));
        var leaseSession = await leaseFixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(leaseFixture.Tokens.TryValidate(leaseSession.AccessToken, SessionAudiences.Ui, out var leaseContext));
        var leaseWriter = new CapturingServerStreamWriter<SurfaceFeedEvent>();
        var leaseRequest = FeedRequest();
        var leaseTask = leaseFixture.Service.WatchSurfaceFeed(leaseRequest, leaseWriter, AuthContext(leaseSession.AccessToken));
        await WaitForMessages(leaseWriter, 1);
        var leaseError = await Assert.ThrowsAsync<RpcException>(async () => await leaseTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(StatusCode.Unauthenticated, leaseError.StatusCode);
        leaseFixture.Producer.Republish(leaseContext, "after-expiry");
        Assert.Single(leaseWriter.Messages);

        var revokedFixture = CreateService(new UiDeliveryOptions(TimeSpan.FromMinutes(4), TimeSpan.FromMilliseconds(25)));
        var revokedSession = await revokedFixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(revokedFixture.Tokens.TryValidate(revokedSession.AccessToken, SessionAudiences.Ui, out var revokedContext));
        var revokedWriter = new CapturingServerStreamWriter<SurfaceFeedEvent>();
        var revokedTask = revokedFixture.Service.WatchSurfaceFeed(FeedRequest(), revokedWriter, AuthContext(revokedSession.AccessToken));
        await WaitForMessages(revokedWriter, 1);
        Assert.True(revokedFixture.Sessions.Revoke(revokedSession.RefreshToken, SessionAudiences.Ui));
        revokedFixture.Producer.Republish(revokedContext, "after-logout");
        var revokedError = await Assert.ThrowsAsync<RpcException>(async () => await revokedTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(StatusCode.Unauthenticated, revokedError.StatusCode);
        Assert.Single(revokedWriter.Messages);
    }

    [Fact]
    public async Task Reconnect_at_latest_after_executor_restart_rematerializes_a_fresh_usable_token()
    {
        var fixture = CreateService();
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        var record = fixture.Producer.EnsureInitial(authenticated);
        var oldToken = ActionToken(new SurfaceEnvelopeWriter(fixture.Actions).Write(
            authenticated, record, Capabilities.ToHashSet(StringComparer.Ordinal)));

        var restarted = RebuildService(fixture);
        using var cancellation = new CancellationTokenSource();
        var writer = new CapturingServerStreamWriter<SurfaceFeedEvent>(cancellation.Cancel);
        var request = FeedRequest(record.Sequence);
        await restarted.Service.WatchSurfaceFeed(request, writer, AuthContext(session.AccessToken, cancellation.Token));

        var reset = Assert.Single(writer.Messages).Reset;
        Assert.Equal(record.Sequence, reset.ResumeSequence);
        var freshToken = ActionToken(Assert.Single(reset.SnapshotJson));
        Assert.NotEqual(oldToken, freshToken);
        Assert.Equal(WorkspaceSurfaceProducer.InoActionType,
            restarted.Actions.Use(authenticated, WorkspaceSurfaceProducer.InoBindingId, freshToken,
                record.SurfaceId, record.Revision, PromptInput()).ActionType);
        Assert.Equal(ActionRejection.Forged,
            Assert.Throws<ActionRejectedException>(() => restarted.Actions.Use(authenticated,
                WorkspaceSurfaceProducer.InoBindingId, oldToken, record.SurfaceId, record.Revision,
                PromptInput())).Reason);
    }

    [Fact]
    public async Task Open_stream_renews_action_tokens_without_advancing_sequence_and_overlap_is_replay_safe()
    {
        var fixture = CreateService(new UiDeliveryOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20)));
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        using var cancellation = new CancellationTokenSource();
        var writer = new CapturingServerStreamWriter<SurfaceFeedEvent>();
        var stream = fixture.Service.WatchSurfaceFeed(FeedRequest(), writer, AuthContext(session.AccessToken, cancellation.Token));
        await WaitForMessages(writer, 2);
        cancellation.Cancel();
        await stream;

        var messages = writer.Messages.ToArray();
        var first = Assert.Single(messages.Where(static message => message.EventCase == SurfaceFeedEvent.EventOneofCase.SurfaceJson));
        var resets = messages.Where(static message => message.EventCase == SurfaceFeedEvent.EventOneofCase.Reset).ToArray();
        Assert.NotEmpty(resets);
        var renewal = resets[0];
        using var firstEnvelope = JsonDocument.Parse(first.SurfaceJson);
        using var renewedEnvelope = JsonDocument.Parse(Assert.Single(renewal.Reset.SnapshotJson));
        Assert.Equal(firstEnvelope.RootElement.GetProperty("feedSequence").GetInt64(), renewal.Reset.ResumeSequence);
        Assert.Equal(renewal.Reset.ResumeSequence, renewedEnvelope.RootElement.GetProperty("feedSequence").GetInt64());
        Assert.All(resets, reset => Assert.Equal(renewal.Reset.ResumeSequence, reset.Reset.ResumeSequence));
        var oldToken = firstEnvelope.RootElement.GetProperty("actions")[0].GetProperty("actionToken").GetString()!;
        var freshToken = renewedEnvelope.RootElement.GetProperty("actions")[0].GetProperty("actionToken").GetString()!;
        Assert.NotEqual(oldToken, freshToken);

        var outcomes = await Task.WhenAll(new[] { oldToken, freshToken }.Select(token => Task.Run(() =>
        {
            try
            {
                fixture.Actions.Use(authenticated, WorkspaceSurfaceProducer.InoBindingId, token,
                    WorkspaceSurfaceProducer.HomeSurfaceId, 1, PromptInput());
                return (ActionRejection?)null;
            }
            catch (ActionRejectedException exception)
            {
                return exception.Reason;
            }
        })));
        Assert.Single(outcomes, static outcome => outcome is null);
        Assert.Single(outcomes, static outcome => outcome == ActionRejection.Replay);
        Assert.DoesNotContain(ActionRejection.Forged, outcomes);
    }

    [Fact]
    public async Task Transport_gap_emits_atomic_snapshot_reset_then_honors_cancellation()
    {
        var fixture = CreateService();
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        Assert.True(fixture.Tokens.TryValidate(session.AccessToken, SessionAudiences.Ui, out var authenticated));
        fixture.Producer.EnsureInitial(authenticated);
        fixture.Producer.Republish(authenticated, "command-1");
        fixture.Producer.Republish(authenticated, "command-2");
        fixture.Feed.RetainFrom(authenticated, SurfaceAudienceKind.Principal, 2);

        using var cancellation = new CancellationTokenSource();
        var writer = new CapturingServerStreamWriter<SurfaceFeedEvent>(cancellation.Cancel);
        var request = new WatchSurfaceFeedRequest { AfterSequence = 0, Audience = (FeedAudienceKind)0, MaxBatchSize = 1 };
        request.ClientCapabilities.AddRange(Capabilities);
        await fixture.Service.WatchSurfaceFeed(request, writer, TestServerCallContext.WithHeaders(cancellation.Token,
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", session.AccessToken)));

        var resetEvent = Assert.Single(writer.Messages);
        Assert.Equal(SurfaceFeedEvent.EventOneofCase.Reset, resetEvent.EventCase);
        Assert.Equal(3, resetEvent.Reset.ResumeSequence);
        Assert.Single(resetEvent.Reset.SnapshotJson);
        using var snapshot = JsonDocument.Parse(resetEvent.Reset.SnapshotJson[0]);
        Assert.Equal(3, snapshot.RootElement.GetProperty("feedSequence").GetInt64());
    }

    [Fact]
    public async Task Failed_stream_write_does_not_advance_the_acknowledgement_watermark()
    {
        var fixture = CreateService();
        var session = await fixture.Service.BootstrapSession(
            new BootstrapSessionRequest { Secret = "bootstrap-secret" }, BootstrapContext());
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.WatchSurfaceFeed(
            FeedRequest(), new ThrowingServerStreamWriter<SurfaceFeedEvent>(), AuthContext(session.AccessToken)));

        var error = await Assert.ThrowsAsync<RpcException>(() => fixture.Service.AcknowledgeSurfaceFeed(
            new McpProject::DigitalBrain.V2.Ui.Grpc.AcknowledgeSurfaceFeedRequest
            {
                Audience = (FeedAudienceKind)0,
                Sequence = 1
            }, AuthContext(session.AccessToken)));
        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
    }

    [Fact]
    public async Task Anonymous_malformed_expired_and_wrong_audience_transport_sessions_are_denied()
    {
        var fixture = CreateService();
        var context = Context("server-tenant", "server-workspace", "flutter");
        var expired = fixture.Tokens.Issue(context, TimeSpan.FromMilliseconds(1), SessionAudiences.Ui);
        var wrongAudience = fixture.Tokens.Issue(context, TimeSpan.FromMinutes(5), SessionAudiences.Mcp);

        await AssertUnauthenticated(fixture.Service, TestServerCallContext.Create());
        await AssertUnauthenticated(fixture.Service, TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", "malformed")));
        await AssertUnauthenticated(fixture.Service, TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", expired)));
        await AssertUnauthenticated(fixture.Service, TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", wrongAudience)));
        await AssertUnauthenticated(fixture.Service, TestServerCallContext.WithHeaders(
            ("x-v2-audience", SessionAudiences.Mcp), ("x-v2-session", wrongAudience)));
    }

    [Fact]
    public async Task Actual_ui_endpoint_denies_plaintext_bootstraps_over_https_and_reports_ready()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ui-host-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var key = RandomNumberGenerator.GetBytes(32);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ApplicationName = typeof(UiGrpcService).Assembly.GetName().Name
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Auth:SessionSigningKey"] = Convert.ToBase64String(key),
                ["DigitalBrain:V2:Ui:FeedIntegrityKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["DigitalBrain:V2:Ui:BootstrapSecret"] = "bootstrap-secret",
                ["DigitalBrain:V2:Ui:TenantId"] = "server-tenant",
                ["DigitalBrain:V2:Ui:WorkspaceId"] = "server-workspace",
                ["DigitalBrain:V2:Ui:PrincipalId"] = "flutter",
                ["DigitalBrain:V2:Ui:FeedStorePath"] = Path.Combine(root, "feed.jsonl")
            });
            builder.Services.AddSingleton(new SessionTokenService(key));
            builder.Services.AddSingleton<ISessionManager>(provider =>
                new SessionManager(provider.GetRequiredService<SessionTokenService>()));
            builder.Services.AddSingleton(new ApplicationService());
            UiHostingExtensions.AddUiTransport(
                builder.Services, builder.Configuration, builder.Environment, RuntimeProfile.Development);

            await using var app = builder.Build();
            UiHostingExtensions.MapUiTransport(app);
            await app.StartAsync();

            using var plainRequest = new HttpRequestMessage(HttpMethod.Post,
                "http://localhost/digitalbrain.v2.ui.DigitalBrainV2Ui/BootstrapSession")
            {
                Content = new ByteArrayContent([])
            };
            plainRequest.Content.Headers.ContentType = new("application/grpc");
            using var plainResponse = await app.GetTestClient().SendAsync(plainRequest);
            Assert.Equal(System.Net.HttpStatusCode.UpgradeRequired, plainResponse.StatusCode);

            using var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = app.GetTestServer().CreateHandler()
            });
            var client = new UiGrpcClient(channel);
            var session = await client.BootstrapSessionAsync(
                new BootstrapSessionRequest { Secret = "bootstrap-secret" },
                headers: new Metadata { { "x-v2-audience", SessionAudiences.Ui } });
            Assert.Equal("server-workspace", session.WorkspaceId);
            Assert.Equal(PrincipalScope.Id(new("flutter", PrincipalKind.User)), session.PrincipalId);

            using var webHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, app.GetTestServer().CreateHandler());
            using var webChannel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions { HttpHandler = webHandler });
            var webClient = new UiGrpcClient(webChannel);
            var webSession = await webClient.BootstrapSessionAsync(
                new BootstrapSessionRequest { Secret = "bootstrap-secret" },
                headers: new Metadata { { "x-v2-audience", SessionAudiences.Ui } });
            using var streamCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var feedCall = webClient.WatchSurfaceFeed(
                FeedRequest(),
                headers: new Metadata
                {
                    { "x-v2-audience", SessionAudiences.Ui },
                    { "x-v2-session", webSession.AccessToken }
                },
                cancellationToken: streamCancellation.Token);
            Assert.True(await feedCall.ResponseStream.MoveNext(streamCancellation.Token));
            Assert.Equal(SurfaceFeedEvent.EventOneofCase.SurfaceJson, feedCall.ResponseStream.Current.EventCase);

            var health = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, health.Status);
            Assert.Equal(HealthStatus.Healthy, health.Entries["v2-ui-transport"].Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Feed_integrity_key_is_explicit_stable_and_independent_from_session_key_rotation()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-ui-integrity-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var feedPath = Path.Combine(root, "feed.jsonl");
            var integrityKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var firstConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Auth:SessionSigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["DigitalBrain:V2:Ui:FeedIntegrityKey"] = integrityKey
            }).Build();
            var secondConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Auth:SessionSigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["DigitalBrain:V2:Ui:FeedIntegrityKey"] = integrityKey
            }).Build();

            var first = UiIntegrityKeyProvider.Resolve(firstConfiguration, RuntimeProfile.Test, feedPath);
            var restarted = UiIntegrityKeyProvider.Resolve(secondConfiguration, RuntimeProfile.Test, feedPath);
            Assert.Equal(first, restarted);
            Assert.Throws<InvalidOperationException>(() =>
                UiIntegrityKeyProvider.Resolve(new ConfigurationBuilder().Build(), RuntimeProfile.Test, feedPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ui_admission_host_has_no_legacy_gateway_or_bus_composition()
    {
        var assembly = typeof(UiGrpcService).Assembly;
        var typeNames = assembly.GetTypes().Select(static type => type.FullName ?? type.Name).ToArray();
        Assert.DoesNotContain(typeNames, name => name.EndsWith(".GatewayService", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, name => name.EndsWith(".UiGatewayService", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, name => name.EndsWith(".HomeFeedBus", StringComparison.Ordinal));
        Assert.DoesNotContain(typeNames, name => name.EndsWith(".SignalEgressBus", StringComparison.Ordinal));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            string.Equals(reference.Name, "DigitalBrain.Kernel", StringComparison.Ordinal));
    }

    private static async Task AssertUnauthenticated(UiGrpcService service, TestServerCallContext context)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.AcknowledgeSurfaceFeed(
            new McpProject::DigitalBrain.V2.Ui.Grpc.AcknowledgeSurfaceFeedRequest { Sequence = 0 }, context));
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    private static WatchSurfaceFeedRequest FeedRequest(long afterSequence = 0)
    {
        var request = new WatchSurfaceFeedRequest
        {
            AfterSequence = afterSequence,
            Audience = (FeedAudienceKind)0,
            MaxBatchSize = 10
        };
        request.ClientCapabilities.AddRange(Capabilities);
        return request;
    }

    private static TestServerCallContext AuthContext(string accessToken, CancellationToken cancellationToken = default) =>
        TestServerCallContext.WithHeaders(cancellationToken,
            ("x-v2-audience", SessionAudiences.Ui), ("x-v2-session", accessToken));

    private static TestServerCallContext BootstrapContext() =>
        TestServerCallContext.WithHeaders(("x-v2-audience", SessionAudiences.Ui));

    private static async Task WaitForMessages(CapturingServerStreamWriter<SurfaceFeedEvent> writer, int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (writer.Messages.Count < count && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(writer.Messages.Count >= count, $"Expected {count} feed messages but received {writer.Messages.Count}.");
    }

    private static ServiceFixture RebuildService(ServiceFixture prior)
    {
        var actions = new ActionExecutor(prior.Feed);
        var producer = new WorkspaceSurfaceProducer(prior.Feed, actions);
        var service = new UiGrpcService(
            new UiBootstrapAuthenticator(prior.BootstrapOptions, prior.Sessions),
            prior.Sessions,
            prior.Tokens,
            prior.Feed,
            producer,
            new SurfaceEnvelopeWriter(actions),
            actions,
            prior.Application,
            prior.DeliveryOptions,
            NullLogger<UiGrpcService>.Instance);
        return new(service, prior.Tokens, prior.Feed, producer, actions, prior.Application, prior.Sessions,
            prior.BootstrapOptions, prior.DeliveryOptions);
    }

    private static ServiceFixture CreateService(
        UiDeliveryOptions? deliveryOptions = null,
        TimeSpan? accessLifetime = null,
        ApplicationService? application = null)
    {
        var tokens = new SessionTokenService(RandomNumberGenerator.GetBytes(32));
        var sessions = new SessionManager(tokens, TimeSpan.FromHours(1));
        var options = new UiBootstrapOptions(
            "bootstrap-secret",
            new("server-tenant"),
            new("server-workspace"),
            new("flutter", PrincipalKind.User),
            accessLifetime ?? TimeSpan.FromMinutes(15),
            new HashSet<string> { "brain.read", "ui.action" });
        var feed = new PrivateFeedStore();
        var actions = new ActionExecutor(feed);
        var producer = new WorkspaceSurfaceProducer(feed, actions);
        application ??= new ApplicationService();
        var delivery = (deliveryOptions ?? UiDeliveryOptions.Default).Validate();
        var service = new UiGrpcService(
            new UiBootstrapAuthenticator(options, sessions),
            sessions,
            tokens,
            feed,
            producer,
            new SurfaceEnvelopeWriter(actions),
            actions,
            application,
            delivery,
            NullLogger<UiGrpcService>.Instance);
        return new(service, tokens, feed, producer, actions, application, sessions, options, delivery);
    }

    private sealed record ServiceFixture(
        UiGrpcService Service,
        SessionTokenService Tokens,
        PrivateFeedStore Feed,
        WorkspaceSurfaceProducer Producer,
        ActionExecutor Actions,
        ApplicationService Application,
        SessionManager Sessions,
        UiBootstrapOptions BootstrapOptions,
        UiDeliveryOptions DeliveryOptions);

    private sealed class ThrowingServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.FromException(new IOException("injected stream write failure"));
    }

    private sealed class ProjectingInoTestHandler(WorkspaceSurfaceProducer producer) : ICommandHandler
    {
        public bool CanHandle(string commandType) =>
            string.Equals(commandType, WorkspaceSurfaceProducer.InoActionType, StringComparison.Ordinal);

        public Task<CommandExecutionResult> ExecuteAsync(
            CommandEnvelope command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            producer.Republish(command.Context, "transport-test");
            return Task.FromResult(CommandExecutionResult.Success());
        }
    }

    private static JsonElement PromptInput() => JsonDocument.Parse(PromptInputJson).RootElement.Clone();

    private static RuntimeRequestContext Context(string tenant, string workspace, string principal) => new(
        new(tenant),
        new(workspace),
        new(principal, PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        null,
        new HashSet<string>(StringComparer.Ordinal) { "brain.read", "ui.action" });

    private static string ActionToken(string envelopeJson)
    {
        using var document = JsonDocument.Parse(envelopeJson);
        return document.RootElement.GetProperty("actions")[0].GetProperty("actionToken").GetString()!;
    }
}
