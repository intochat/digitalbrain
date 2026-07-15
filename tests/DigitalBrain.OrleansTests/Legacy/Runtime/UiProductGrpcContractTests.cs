extern alias McpProject;

using DigitalBrainV2Ui = McpProject::DigitalBrain.V2.Ui.Grpc.DigitalBrainV2Ui;
using UiGrpcService = McpProject::DigitalBrain.Mcp.UiGrpcService;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiProductGrpcContractTests
{
    private static readonly string[] ExistingMethods =
    [
        "BootstrapSession",
        "RefreshSession",
        "WatchSurfaceFeed",
        "AcknowledgeSurfaceFeed",
        "SubmitAction",
        "LogoutSession"
    ];

    private static readonly string[] ProductMethods =
    [
        "GetFeatureDraft",
        "ResetFeatureDraftInstallation",
        "ReviseFeatureDraft",
        "SuggestFeatureChange",
        "VerifyFeatureDraft",
        "ReviewFeatureAccess",
        "InstallFeatureVersion",
        "ResumeOriginatingRequest",
        "ListFeatures",
        "GetFeature",
        "GetFeatureReleaseSource",
        "RollbackFeatureVersion",
        "ListConnections",
        "GetConnection",
        "ListActivity",
        "GetRun",
        "ListMemoryItems",
        "GetMemoryItem",
        "GetHomeSummary"
    ];

    [Fact]
    public void Descriptor_exposes_only_the_authoritative_product_methods()
    {
        var additions = DigitalBrainV2Ui.Descriptor.Methods
            .Select(method => method.Name)
            .Except(ExistingMethods, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ProductMethods.Order(StringComparer.Ordinal), additions);
        Assert.DoesNotContain(additions, method => method is
            "UpdateFeatureDraft" or
            "SuggestFeatureDraftPatch" or
            "InstallFeatureDraft" or
            "ResumeFeatureRequest" or
            "ListConnectors" or
            "GetConnector" or
            "ListRuns" or
            "ListMemory" or
            "GetHome");
    }

    [Fact]
    public void Governed_authoring_installation_and_release_detail_methods_are_overridden()
    {
        var declaredProductMethods = typeof(UiGrpcService)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(UiGrpcService))
            .Select(method => method.Name)
            .Intersect(ProductMethods, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "GetFeatureDraft",
                "ResetFeatureDraftInstallation",
                "ReviseFeatureDraft",
                "SuggestFeatureChange",
                "VerifyFeatureDraft",
                "ReviewFeatureAccess",
                "InstallFeatureVersion",
                "ResumeOriginatingRequest",
                "GetFeature",
                "GetFeatureReleaseSource",
                "RollbackFeatureVersion"
            }.Order(StringComparer.Ordinal),
            declaredProductMethods);
    }

    [Fact]
    public void Product_requests_permanently_reserve_caller_identity_fields()
    {
        var methods = DigitalBrainV2Ui.Descriptor.Methods
            .Where(method => ProductMethods.Contains(method.Name, StringComparer.Ordinal));
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in methods)
        {
            var request = method.InputType.ToProto();
            var reply = method.OutputType.ToProto();

            Assert.Contains(request.ReservedRange, range => range.Start <= 1 && range.End > 1);
            Assert.Contains(request.ReservedRange, range => range.Start <= 2 && range.End > 2);
            Assert.Contains("owner_id", request.ReservedName);
            Assert.Contains("actor_id", request.ReservedName);
            Assert.All(request.Field, field => Assert.True(field.Number >= 3));
            Assert.Contains("owner_id", reply.ReservedName);
            Assert.Contains("actor_id", reply.ReservedName);
            DemandRecursiveIdentityReservation(method.InputType, visited);
            DemandRecursiveIdentityReservation(method.OutputType, visited);
        }
    }

    [Fact]
    public void Deferred_methods_publish_no_speculative_product_projections()
    {
        var implemented = new HashSet<string>(
            [
                "GetFeatureDraft",
                "ResetFeatureDraftInstallation",
                "ReviseFeatureDraft",
                "SuggestFeatureChange",
                "VerifyFeatureDraft",
                "ReviewFeatureAccess",
                "InstallFeatureVersion",
                "ResumeOriginatingRequest",
                "GetFeature",
                "GetFeatureReleaseSource",
                "RollbackFeatureVersion"
            ],
            StringComparer.Ordinal);
        var deferred = ProductMethods.Where(method => !implemented.Contains(method)).ToHashSet(StringComparer.Ordinal);
        var methods = DigitalBrainV2Ui.Descriptor.Methods.Where(method => deferred.Contains(method.Name));
        var speculativeMessages = new[]
        {
            "FeatureSummary",
            "FeatureReleaseSummary",
            "ConnectionSummary",
            "FeatureRunSnapshot",
            "MemoryItemSummary",
            "MemoryItem",
            "HomeAttentionItem",
            "AutomationSummary"
        };
        var messageNames = DigitalBrainV2Ui.Descriptor.File.MessageTypes.Select(message => message.Name).ToArray();

        Assert.All(methods, method => Assert.Empty(method.OutputType.Fields.InDeclarationOrder()));
        Assert.All(speculativeMessages, name => Assert.DoesNotContain(name, messageNames));
    }

    [Fact]
    public void Revision_tokens_distinguish_omitted_from_zero()
    {
        var messages = DigitalBrainV2Ui.Descriptor.File.MessageTypes.ToDictionary(message => message.Name);
        var revisionFields = new[]
        {
            ("ReviseFeatureDraftRequest", "expected_revision"),
            ("SuggestFeatureChangeRequest", "expected_revision"),
            ("VerifyFeatureDraftRequest", "expected_revision"),
            ("ReviewFeatureAccessRequest", "expected_revision"),
            ("InstallFeatureVersionRequest", "expected_revision"),
            ("RollbackFeatureVersionRequest", "expected_revision"),
            ("ResumeOriginatingRequestRequest", "expected_revision"),
            ("RejectSuggestedChangeInput", "base_revision"),
            ("FeatureDraftPatch", "base_revision")
        };

        foreach (var (message, field) in revisionFields)
            Assert.True(messages[message].FindFieldByName(field).HasPresence, $"{message}.{field}");
    }

    [Fact]
    public void Draft_reply_additively_exposes_exact_installation_recovery_wire_contract()
    {
        var messages = DigitalBrainV2Ui.Descriptor.File.MessageTypes.ToDictionary(message => message.Name);
        var draftReply = messages["FeatureDraftReply"];

        Assert.True(messages.TryGetValue("FeatureInstallationRecovery", out var recovery));
        var recoveryField = draftReply.FindFieldByName("recovery");
        Assert.Equal(2, recoveryField.FieldNumber);
        Assert.Equal(recovery, recoveryField.MessageType);
        Assert.Equal(
            new[]
            {
                ("installed", 1),
                ("verification", 2),
                ("release", 3),
                ("installation_id", 4),
                ("grants", 5),
                ("subscriptions", 6),
                ("previous_release", 7),
                ("decision_id", 8),
                ("idempotency_id", 9),
                ("rollback_available", 10),
                ("paused", 11),
                ("pause_reason", 12)
            },
            recovery!.Fields.InDeclarationOrder().Select(field => (field.Name, field.FieldNumber)));
        Assert.Equal(Google.Protobuf.Reflection.FieldType.Bool, recovery.FindFieldByName("installed").FieldType);
        Assert.Equal("FeatureVerification", recovery.FindFieldByName("verification").MessageType.Name);
        Assert.Equal("FeatureRelease", recovery.FindFieldByName("release").MessageType.Name);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, recovery.FindFieldByName("installation_id").FieldType);
        Assert.True(recovery.FindFieldByName("grants").IsRepeated);
        Assert.Equal("FeatureGrant", recovery.FindFieldByName("grants").MessageType.Name);
        Assert.True(recovery.FindFieldByName("subscriptions").IsRepeated);
        Assert.Equal("FeatureRelease", recovery.FindFieldByName("previous_release").MessageType.Name);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, recovery.FindFieldByName("decision_id").FieldType);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, recovery.FindFieldByName("idempotency_id").FieldType);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.Bool, recovery.FindFieldByName("rollback_available").FieldType);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.Bool, recovery.FindFieldByName("paused").FieldType);
        Assert.Equal(Google.Protobuf.Reflection.FieldType.String, recovery.FindFieldByName("pause_reason").FieldType);
        Assert.Equal(
            new[] { "previous_release", "decision_id", "idempotency_id", "pause_reason" },
            recovery.ToProto().Field.Where(field => field.Proto3Optional).Select(field => field.Name));
        Assert.False(recovery.FindFieldByName("installed").HasPresence);
        Assert.False(recovery.FindFieldByName("rollback_available").HasPresence);
        Assert.False(recovery.FindFieldByName("paused").HasPresence);
        Assert.Contains("owner_id", recovery.ToProto().ReservedName);
        Assert.Contains("actor_id", recovery.ToProto().ReservedName);
    }

    [Fact]
    public void Legacy_originating_conversation_identity_has_explicit_presence()
    {
        var originatingRequest = DigitalBrainV2Ui.Descriptor.File.MessageTypes.Single(message =>
            message.Name == "OriginatingRequest");

        Assert.True(originatingRequest.FindFieldByName("conversation_id").HasPresence);
    }

    [Fact]
    public void Public_install_and_feature_selectors_exclude_internal_authority_coordinates()
    {
        var messages = DigitalBrainV2Ui.Descriptor.File.MessageTypes.ToDictionary(message => message.Name);
        var install = messages["FeatureInstallReply"];
        var selector = messages["GetFeatureRequest"];

        Assert.Null(install.FindFieldByName("active_grant_revision"));
        Assert.Equal(6, install.FindFieldByName("rollback_available").FieldNumber);
        Assert.Equal(7, install.FindFieldByName("paused").FieldNumber);
        Assert.Equal(8, install.FindFieldByName("pause_reason").FieldNumber);
        Assert.Equal("feature_id", Assert.Single(selector.Fields.InDeclarationOrder()).Name);
    }

    [Fact]
    public void Verification_review_release_detail_and_rollback_publish_exact_safe_fields()
    {
        var messages = DigitalBrainV2Ui.Descriptor.File.MessageTypes.ToDictionary(message => message.Name);
        var verification = messages["FeatureVerification"];
        var release = messages["FeatureRelease"];
        var review = messages["FeatureAccessReviewReply"];
        var detail = messages["FeatureReply"];
        var sourceRequest = messages["GetFeatureReleaseSourceRequest"];
        var sourceReply = messages["FeatureReleaseSourceReply"];
        var rollback = messages["RollbackFeatureVersionRequest"];

        Assert.NotNull(verification.FindFieldByName("scenarios"));
        Assert.NotNull(verification.FindFieldByName("artifacts"));
        Assert.NotNull(verification.FindFieldByName("source_reference"));
        Assert.NotNull(release.FindFieldByName("source_reference"));
        Assert.NotNull(release.FindFieldByName("source"));
        Assert.NotNull(review.FindFieldByName("grants"));
        Assert.NotNull(review.FindFieldByName("subscriptions"));
        Assert.NotNull(review.FindFieldByName("previous_release"));
        Assert.NotNull(detail.FindFieldByName("originating_request"));
        Assert.NotNull(detail.FindFieldByName("active_release"));
        Assert.NotNull(detail.FindFieldByName("previous_release"));
        Assert.NotNull(detail.FindFieldByName("rollback_available"));
        Assert.NotNull(detail.FindFieldByName("installation_id"));
        Assert.NotNull(detail.FindFieldByName("revision"));
        Assert.Equal("feature_id", sourceRequest.FindFieldByNumber(3).Name);
        Assert.Equal("installation_id", sourceRequest.FindFieldByNumber(4).Name);
        Assert.Equal("release_digest", sourceRequest.FindFieldByNumber(5).Name);
        Assert.Equal("source_reference", sourceRequest.FindFieldByNumber(6).Name);
        Assert.NotNull(sourceReply.FindFieldByName("feature_id"));
        Assert.NotNull(sourceReply.FindFieldByName("installation_id"));
        Assert.NotNull(sourceReply.FindFieldByName("release_digest"));
        Assert.NotNull(sourceReply.FindFieldByName("source_reference"));
        Assert.NotNull(sourceReply.FindFieldByName("source"));
        Assert.NotNull(rollback.FindFieldByName("expected_active_digest"));
        Assert.NotNull(rollback.FindFieldByName("target_digest"));
        Assert.NotNull(rollback.FindFieldByName("expected_revision"));
        Assert.NotNull(rollback.FindFieldByName("idempotency_id"));
        Assert.Null(detail.FindFieldByName("active_grant_revision"));
        Assert.Null(detail.FindFieldByName("hub_revision"));
    }

    private static void DemandRecursiveIdentityReservation(
        Google.Protobuf.Reflection.MessageDescriptor message,
        HashSet<string> visited)
    {
        if (!visited.Add(message.FullName))
            return;
        var proto = message.ToProto();
        Assert.Contains("owner_id", proto.ReservedName);
        Assert.Contains("actor_id", proto.ReservedName);
        foreach (var field in message.Fields.InDeclarationOrder().Where(field =>
                     field.FieldType is Google.Protobuf.Reflection.FieldType.Message or Google.Protobuf.Reflection.FieldType.Group))
            DemandRecursiveIdentityReservation(field.MessageType, visited);
    }
}
