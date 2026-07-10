extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using IV2McpGmailToolGateway = McpProject::DigitalBrain.Mcp.IV2McpGmailToolGateway;
using V2InoEffectStore = McpProject::DigitalBrain.Mcp.V2InoEffectStore;
using V2McpAuthorizedToolCatalog = McpProject::DigitalBrain.Mcp.V2McpAuthorizedToolCatalog;
using V2McpConversationContextAssembler = McpProject::DigitalBrain.Mcp.V2McpConversationContextAssembler;
using V2McpGmailPlanner = McpProject::DigitalBrain.Mcp.V2McpGmailPlanner;
using V2McpInoCommandHandler = McpProject::DigitalBrain.Mcp.V2McpInoCommandHandler;
using V2McpResponseComposer = McpProject::DigitalBrain.Mcp.V2McpResponseComposer;

namespace DigitalBrain.Tests.V2;

public sealed class V2GmailToolTests
{
    [Fact]
    public async Task Planner_only_selects_the_read_only_latest_gmail_tool_for_eligible_requests()
    {
        var planner = new V2McpGmailPlanner();

        var selected = await planner.PlanAsync(Request("Can you get my last incoming gmail?"));
        var send = await planner.PlanAsync(Request("Send an email to the team"));
        var lastSent = await planner.PlanAsync(Request("Show my last sent email"));

        var invocation = Assert.Single(selected);
        Assert.Equal(V2GmailTools.ReadLatest, invocation.ToolId);
        Assert.Empty(invocation.Input.EnumerateObject());
        Assert.Empty(send);
        Assert.Empty(lastSent);
    }

    [Fact]
    public async Task Authorized_catalog_reads_only_the_authenticated_principal_scope_and_requires_permission()
    {
        var gateway = new RecordingGateway(new(V2GmailReadStatus.Success, "A real mailbox preview."));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var invocation = Invocation();
        var first = Context("user-a", "workspace-a", "gmail.read");
        var second = Context("user-b", "workspace-a", "gmail.read");

        var firstResult = await catalog.InvokeAsync(first, invocation);
        var secondResult = await catalog.InvokeAsync(second, invocation);
        var denied = await catalog.InvokeAsync(Context("user-a", "workspace-a"), invocation);

        Assert.Equal(V2ToolOutcomeKind.Success, firstResult.Kind);
        Assert.Equal("A real mailbox preview.", firstResult.Content!.Value.GetProperty("latestMessage").GetString());
        Assert.Equal(V2ToolOutcomeKind.Success, secondResult.Kind);
        Assert.Equal(2, gateway.OwnerScopes.Count);
        Assert.Equal(V2RequestScope.Id(first), gateway.OwnerScopes[0]);
        Assert.Equal(V2RequestScope.Id(second), gateway.OwnerScopes[1]);
        Assert.NotEqual(gateway.OwnerScopes[0], gateway.OwnerScopes[1]);
        Assert.Equal(V2ToolOutcomeKind.Denied, denied.Kind);
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task Disconnected_principal_gets_a_validated_connection_action_instead_of_model_fabrication()
    {
        var gateway = new RecordingGateway(new(
            V2GmailReadStatus.NeedsAuth,
            SafeReason: "Connect your Google account to let INO read your Gmail.",
            ConnectionUrl: "https://accounts.google.com/o/oauth2/v2/auth?state=test"));
        var catalog = new V2McpAuthorizedToolCatalog(gateway);
        var context = Context("user-a", "workspace-a", "gmail.read");
        var outcome = await catalog.InvokeAsync(context, Invocation());
        var composer = new V2McpResponseComposer();

        var text = await composer.ComposeAsync(
            context,
            new V2ModelResponse("I cannot access external email services.", "test", false),
            [outcome]);

        Assert.Equal(V2ToolOutcomeKind.NeedsAuth, outcome.Kind);
        Assert.Equal("Connect your Google account to let INO read your Gmail.", text);
        Assert.Equal("Connect Google", outcome.Action?.Label);
        Assert.StartsWith("https://accounts.google.com/", outcome.Action?.Target, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conversation_model_receives_the_actual_tool_outcome_and_response_is_grounded_in_it()
    {
        var context = Context("user-a", "workspace-a", "gmail.read");
        var gateway = new RecordingGateway(new(V2GmailReadStatus.Success, "The build finished successfully."));
        V2ModelRequest? received = null;
        var owner = new V2ConversationOwner(
            new StaticContextAssembler(),
            new V2McpGmailPlanner(),
            new RecordingModelRouter(request =>
            {
                received = request;
                var latest = request.ToolOutcomes!.Single().Content!.Value.GetProperty("latestMessage").GetString();
                return new V2ModelResponse("Your latest email says: " + latest, "test", false);
            }),
            new V2McpAuthorizedToolCatalog(gateway),
            new V2McpResponseComposer());

        var result = await owner.ExecuteDetailedAsync(new V2ConversationRequest(
            context,
            "conversation",
            "What is my latest Gmail?"));

        Assert.Single(received!.ToolOutcomes!);
        Assert.Equal(V2ToolOutcomeKind.Success, received.ToolOutcomes![0].Kind);
        Assert.Contains("The build finished successfully.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_command_replay_does_not_fetch_gmail_twice()
    {
        var context = Context("user-a", "workspace-a", "gmail.read", "brain.act", "ui.action");
        var store = new V2InoEffectStore();
        var feed = new V2PrivateFeedStore();
        var surfaces = new V2WorkspaceSurfaceProducer(feed, new V2ActionExecutor(feed), store);
        var gateway = new RecordingGateway(new(V2GmailReadStatus.Success, "One durable preview."));
        var owner = new V2ConversationOwner(
            new V2McpConversationContextAssembler(store),
            new V2McpGmailPlanner(),
            new RecordingModelRouter(request => new V2ModelResponse(
                request.ToolOutcomes!.Single().Content!.Value.GetProperty("latestMessage").GetString()!,
                "test",
                false)),
            new V2McpAuthorizedToolCatalog(gateway),
            new V2McpResponseComposer());
        var handler = new V2McpInoCommandHandler(store, surfaces, owner);
        var command = new V2CommandEnvelope(
            V2McpInoCommandHandler.CommandType,
            2,
            "stable-gmail-command",
            context,
            JsonSerializer.SerializeToElement(new { prompt = "Get my latest Gmail" }));

        Assert.Equal(WorkflowState.Succeeded, (await handler.ExecuteAsync(command)).State);
        Assert.Equal(WorkflowState.Succeeded, (await handler.ExecuteAsync(command)).State);

        Assert.Equal(1, gateway.Calls);
        Assert.Equal(2, store.Read(context).Turns.Count);
    }

    private static V2ConversationRequest Request(string text) =>
        new(Context("user", "workspace", "gmail.read"), "conversation", text);

    private static V2ToolInvocation Invocation() =>
        new(V2GmailTools.ReadLatest, JsonSerializer.SerializeToElement(new { }));

    private static V2RequestContext Context(string principal, string workspace, params string[] grants) => new(
        new TenantId("tenant"),
        new WorkspaceId(workspace),
        new PrincipalRef(principal, PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "idempotency",
        grants.ToHashSet(StringComparer.Ordinal));

    private sealed class RecordingGateway(V2GmailReadResult result) : IV2McpGmailToolGateway
    {
        public List<string> OwnerScopes { get; } = [];
        public int Calls => OwnerScopes.Count;

        public Task<V2GmailReadResult> ReadLatestIncomingAsync(
            string ownerScope,
            CancellationToken cancellationToken = default)
        {
            OwnerScopes.Add(ownerScope);
            return Task.FromResult(result);
        }
    }

    private sealed class StaticContextAssembler : IV2ContextAssembler
    {
        public Task<V2ConversationContext> AssembleAsync(
            V2ConversationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new V2ConversationContext(
                request.Context.TenantId,
                request.Context.WorkspaceId,
                request.ConversationId,
                []));
    }

    private sealed class RecordingModelRouter(Func<V2ModelRequest, V2ModelResponse> complete) : IV2ModelRouter
    {
        public Task<V2ModelResponse> CompleteAsync(
            V2ModelRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(complete(request));
    }
}
