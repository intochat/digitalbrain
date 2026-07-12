using System.Diagnostics;
using DigitalBrain.Core;
using DigitalBrain.Core.Runtime;
using Microsoft.Extensions.Configuration;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class McpAuthorizedToolCatalog : IAuthorizedToolCatalog
{
    private readonly AuthorizedToolRegistry _tools;

    public McpAuthorizedToolCatalog(
        IMcpIntegrationToolGateway integrations,
        IInoConversationStore? conversations = null,
        IConfiguration? configuration = null,
        ToolActionPolicy? actionPolicy = null)
    {
        var policy = actionPolicy ?? new ToolActionPolicy(
            configuration?["DigitalBrain:Salesforce:RedirectUri"]);
        _tools = AuthorizedToolRegistry.Create(integrations, conversations, policy);
    }

    public async Task<ToolOutcome> InvokeAsync(
        RuntimeRequestContext context,
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        using var activity = InoTelemetry.Source.StartActivity("ino.tool.invoke", ActivityKind.Internal);
        activity?.SetTag("db.ino.tool_id", invocation.ToolId);
        var outcome = await _tools.InvokeAsync(context, invocation, cancellationToken).ConfigureAwait(false);
        activity?.SetTag("db.ino.tool_outcome", outcome.Kind.ToString());
        activity?.SetTag("db.ino.has_grounding", outcome.Kind == ToolOutcomeKind.Success && outcome.Content is not null);
        if (outcome.Kind is ToolOutcomeKind.RetryableFailure or ToolOutcomeKind.PermanentFailure or
            ToolOutcomeKind.OutcomeUnknown)
            activity?.SetStatus(ActivityStatusCode.Error, outcome.Kind.ToString());
        return outcome;
    }
}
