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
        "ReviseFeatureDraft",
        "SuggestFeatureChange",
        "VerifyFeatureDraft",
        "InstallFeatureVersion",
        "ResumeOriginatingRequest",
        "ListFeatures",
        "GetFeature",
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
    public void Only_the_five_authoring_product_methods_are_overridden()
    {
        var declaredProductMethods = typeof(UiGrpcService)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(UiGrpcService))
            .Select(method => method.Name)
            .Intersect(ProductMethods, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ProductMethods.Take(5).Order(StringComparer.Ordinal),
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
        var deferred = ProductMethods.Skip(5).ToHashSet(StringComparer.Ordinal);
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
            ("InstallFeatureVersionRequest", "expected_revision"),
            ("ResumeOriginatingRequestRequest", "expected_revision"),
            ("RejectSuggestedChangeInput", "base_revision"),
            ("FeatureDraftPatch", "base_revision")
        };

        foreach (var (message, field) in revisionFields)
            Assert.True(messages[message].FindFieldByName(field).HasPresence, $"{message}.{field}");
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
