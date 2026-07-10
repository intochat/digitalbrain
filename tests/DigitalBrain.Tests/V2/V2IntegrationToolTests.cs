extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using IV2McpIntegrationToolGateway = McpProject::DigitalBrain.Mcp.IV2McpIntegrationToolGateway;
using V2InoEffectStore = McpProject::DigitalBrain.Mcp.V2InoEffectStore;
using V2McpAuthorizedToolCatalog = McpProject::DigitalBrain.Mcp.V2McpAuthorizedToolCatalog;
using V2McpConversationContextAssembler = McpProject::DigitalBrain.Mcp.V2McpConversationContextAssembler;
using V2McpIntegrationPlanner = McpProject::DigitalBrain.Mcp.V2McpIntegrationPlanner;
using V2McpInoCommandHandler = McpProject::DigitalBrain.Mcp.V2McpInoCommandHandler;
using V2McpResponseComposer = McpProject::DigitalBrain.Mcp.V2McpResponseComposer;

namespace DigitalBrain.Tests.V2;

public sealed class V2IntegrationToolTests
{
    [Fact]
    public async Task Planner_selects_only_supported_read_only_integration_tools()
    {
        var planner = new V2McpIntegrationPlanner();

        var gmail = await planner.PlanAsync(Request("Can you get my last incoming gmail?"));
        var salesforce = await planner.PlanAsync(Request("Show my latest Salesforce account"));
        var send = await planner.PlanAsync(Request("Send an email to the team"));
        var update = await planner.PlanAsync(Request("Update my Salesforce account"));

        Assert.Equal(V2GmailTools.ReadLatest, Assert.Single(gmail).ToolId);
        Assert.Equal(V2SalesforceTools.ReadLatestAccount, Assert.Single(salesforce).ToolId);
        Assert.Empty(send);
        Assert.Empty(update);
    }

    [Fact]
    public async Task Gmail_catalog_uses_authenticated_principal_scope_and_requires_permission()
    {
        var gateway = new RecordingGateway(gmail: new(V2GmailReadStatus.Success, "A real mailbox preview."));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var first = Context("user-a", "workspace-a", "gmail.read");
        var second = Context("user-b", "workspace-a", "gmail.read");

        var firstResult = await catalog.InvokeAsync(first, GmailInvocation());
        var secondResult = await catalog.InvokeAsync(second, GmailInvocation());
        var denied = await catalog.InvokeAsync(Context("user-a", "workspace-a"), GmailInvocation());

        Assert.Equal(V2ToolOutcomeKind.Success, firstResult.Kind);
        Assert.Equal("A real mailbox preview.", firstResult.Content!.Value.GetProperty("latestMessage").GetString());
        Assert.Equal(V2ToolOutcomeKind.Success, secondResult.Kind);
        Assert.Equal([V2RequestScope.Id(first), V2RequestScope.Id(second)], gateway.GmailOwnerScopes);
        Assert.NotEqual(gateway.GmailOwnerScopes[0], gateway.GmailOwnerScopes[1]);
        Assert.Equal(V2ToolOutcomeKind.Denied, denied.Kind);
    }

    [Fact]
    public async Task Salesforce_catalog_uses_authenticated_principal_scope_and_requires_permission()
    {
        var gateway = new RecordingGateway(salesforce: new(V2SalesforceReadStatus.Success, "{\"Name\":\"Grounded account\"}"));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var first = Context("user-a", "workspace-a", "salesforce.read");
        var second = Context("user-b", "workspace-a", "salesforce.read");

        var firstResult = await catalog.InvokeAsync(first, SalesforceInvocation());
        var secondResult = await catalog.InvokeAsync(second, SalesforceInvocation());
        var denied = await catalog.InvokeAsync(Context("user-a", "workspace-a"), SalesforceInvocation());

        Assert.Equal(V2ToolOutcomeKind.Success, firstResult.Kind);
        Assert.Equal("{\"Name\":\"Grounded account\"}", firstResult.Content!.Value.GetProperty("latestAccount").GetString());
        Assert.Equal(V2ToolOutcomeKind.Success, secondResult.Kind);
        Assert.Equal([V2RequestScope.Id(first), V2RequestScope.Id(second)], gateway.SalesforceOwnerScopes);
        Assert.NotEqual(gateway.SalesforceOwnerScopes[0], gateway.SalesforceOwnerScopes[1]);
        Assert.Equal(V2ToolOutcomeKind.Denied, denied.Kind);
    }

    [Fact]
    public async Task Disconnected_integrations_produce_validated_native_connection_actions()
    {
        var gateway = new RecordingGateway(
            gmail: new(
                V2GmailReadStatus.NeedsAuth,
                SafeReason: "Connect your Google account to let INO read your Gmail.",
                ConnectionUrl: "https://accounts.google.com/o/oauth2/v2/auth?state=test"),
            salesforce: new(
                V2SalesforceReadStatus.NeedsAuth,
                SafeReason: "Connect your Salesforce account to let INO read Salesforce.",
                ConnectionUrl: "https://login.salesforce.com/services/oauth2/authorize?state=test"));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);

        var gmail = await catalog.InvokeAsync(Context("user", "workspace", "gmail.read"), GmailInvocation());
        var salesforce = await catalog.InvokeAsync(Context("user", "workspace", "salesforce.read"), SalesforceInvocation());

        Assert.Equal(V2ToolOutcomeKind.NeedsAuth, gmail.Kind);
        Assert.Equal("Connect Google", gmail.Action?.Label);
        Assert.StartsWith("https://accounts.google.com/", gmail.Action?.Target, StringComparison.Ordinal);
        Assert.Equal(V2ToolOutcomeKind.NeedsAuth, salesforce.Kind);
        Assert.Equal("Connect Salesforce", salesforce.Action?.Label);
        Assert.StartsWith("https://login.salesforce.com/", salesforce.Action?.Target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_application_configuration_is_not_classified_as_provider_outage()
    {
        var gateway = new RecordingGateway(
            gmail: new(V2GmailReadStatus.ConfigurationMissing, SafeReason: "Gmail application configuration is missing."),
            salesforce: new(V2SalesforceReadStatus.ConfigurationMissing, SafeReason: "Salesforce application configuration is missing."));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);

        var gmail = await catalog.InvokeAsync(Context("user", "workspace", "gmail.read"), GmailInvocation());
        var salesforce = await catalog.InvokeAsync(Context("user", "workspace", "salesforce.read"), SalesforceInvocation());

        Assert.Equal(V2ToolOutcomeKind.PermanentFailure, gmail.Kind);
        Assert.Contains("configuration is missing", gmail.SafeReason, StringComparison.Ordinal);
        Assert.Equal(V2ToolOutcomeKind.PermanentFailure, salesforce.Kind);
        Assert.Contains("configuration is missing", salesforce.SafeReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gmail")]
    [InlineData("salesforce")]
    public async Task Model_receives_actual_structured_tool_outcome_and_exact_replay_fetches_once(string provider)
    {
        var isGmail = provider == "gmail";
        var context = Context(
            "user-a",
            "workspace-a",
            isGmail ? "gmail.read" : "salesforce.read",
            "brain.act",
            "ui.action");
        var store = new V2InoEffectStore();
        var feed = new V2PrivateFeedStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var gateway = new RecordingGateway(
            gmail: new(V2GmailReadStatus.Success, "Grounded mailbox preview."),
            salesforce: new(V2SalesforceReadStatus.Success, "{\"Name\":\"Grounded account\"}"));
        V2ModelRequest? received = null;
        var owner = new V2ConversationOwner(
            new V2McpConversationContextAssembler(store),
            new V2McpIntegrationPlanner(),
            new RecordingModelRouter(request =>
            {
                received = request;
                return new V2ModelResponse(request.ToolOutcomes!.Single().Content!.Value.GetRawText(), "test", false);
            }),
            new V2McpAuthorizedToolCatalog(gateway),
            new V2McpResponseComposer());
        var handler = new V2McpInoCommandHandler(store, surfaces, owner);
        var command = new V2CommandEnvelope(
            V2McpInoCommandHandler.CommandType,
            2,
            "stable-" + provider + "-command",
            context,
            JsonSerializer.SerializeToElement(new
            {
                prompt = isGmail ? "Get my latest Gmail" : "Show my latest Salesforce account"
            }));

        Assert.Equal(WorkflowState.Succeeded, (await handler.ExecuteAsync(command)).State);
        Assert.Equal(WorkflowState.Succeeded, (await handler.ExecuteAsync(command)).State);

        Assert.Single(received!.ToolOutcomes!);
        Assert.Equal(V2ToolOutcomeKind.Success, received.ToolOutcomes![0].Kind);
        Assert.Equal(1, isGmail ? gateway.GmailOwnerScopes.Count : gateway.SalesforceOwnerScopes.Count);
        Assert.Equal(2, store.Read(context).Turns.Count);
    }

    private static V2ConversationRequest Request(string text) =>
        new(Context("user", "workspace", "gmail.read", "salesforce.read"), "conversation", text);

    private static V2ToolInvocation GmailInvocation() =>
        new(V2GmailTools.ReadLatest, JsonSerializer.SerializeToElement(new { }));

    private static V2ToolInvocation SalesforceInvocation() =>
        new(V2SalesforceTools.ReadLatestAccount, JsonSerializer.SerializeToElement(new { }));

    private static V2RequestContext Context(string principal, string workspace, params string[] grants) => new(
        new TenantId("tenant"),
        new WorkspaceId(workspace),
        new PrincipalRef(principal, PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "idempotency",
        grants.ToHashSet(StringComparer.Ordinal));

    private sealed class RecordingGateway(
        V2GmailReadResult? gmail = null,
        V2SalesforceReadResult? salesforce = null) : IV2McpIntegrationToolGateway
    {
        public List<string> GmailOwnerScopes { get; } = [];
        public List<string> SalesforceOwnerScopes { get; } = [];

        public Task<V2GmailReadResult> ReadLatestIncomingAsync(
            string ownerScope,
            CancellationToken cancellationToken = default)
        {
            GmailOwnerScopes.Add(ownerScope);
            return Task.FromResult(gmail ?? new V2GmailReadResult(V2GmailReadStatus.Unavailable));
        }

        public Task<V2SalesforceReadResult> ReadLatestSalesforceAccountAsync(
            string ownerScope,
            CancellationToken cancellationToken = default)
        {
            SalesforceOwnerScopes.Add(ownerScope);
            return Task.FromResult(salesforce ?? new V2SalesforceReadResult(V2SalesforceReadStatus.Unavailable));
        }
    }

    private sealed class RecordingModelRouter(Func<V2ModelRequest, V2ModelResponse> complete) : IV2ModelRouter
    {
        public Task<V2ModelResponse> CompleteAsync(
            V2ModelRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(complete(request));
    }
}
