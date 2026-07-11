extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;
using IV2SemanticIntentResolver = McpProject::DigitalBrain.Mcp.IV2SemanticIntentResolver;
using V2McpIntegrationPlanner = McpProject::DigitalBrain.Mcp.V2McpIntegrationPlanner;

namespace DigitalBrain.Tests.V2;

public sealed class V2ConversationalCapabilityBenchmarkTests
{
    public static TheoryData<string, V2SemanticIntentProposal, string> DirectReadCases => new()
    {
        {
            "Get my last 2 incoming emails.",
            Gmail(V2SemanticOperation.List, limit: 2),
            V2GmailTools.ReadMessages
        },
        {
            "gimme my two newest mails pls",
            Gmail(V2SemanticOperation.List, limit: 2),
            V2GmailTools.ReadMessages
        },
        {
            "Only unread.",
            Gmail(
                V2SemanticOperation.Refine,
                reference: V2SemanticReference.LatestProviderResult,
                filters: [new("read state", V2SemanticFilterOperator.Equals, "unread")]),
            V2GmailTools.ReadMessages
        },
        {
            "Now the one before those.",
            Gmail(
                V2SemanticOperation.Previous,
                ordinal: 1,
                reference: V2SemanticReference.LatestProviderResult),
            V2GmailTools.ReadMessages
        },
        {
            "Show emails from that sender last week.",
            Gmail(
                V2SemanticOperation.Refine,
                reference: V2SemanticReference.SameSender,
                timeRange: V2SemanticTimeRange.PreviousWeek),
            V2GmailTools.ReadMessages
        },
        {
            "Which have attachments?",
            Gmail(
                V2SemanticOperation.Refine,
                reference: V2SemanticReference.LatestProviderResult,
                filters: [new("attachment", V2SemanticFilterOperator.Equals, "present")]),
            V2GmailTools.ReadMessages
        },
        {
            "Give me an inbox and unread overview.",
            Gmail(V2SemanticOperation.Overview),
            V2GmailTools.ReadMailboxOverview
        },
        {
            "Find the first matching thread.",
            Gmail(V2SemanticOperation.Threads, limit: 1),
            V2GmailTools.ReadThreads
        },
        {
            "sumarize the first thread",
            Gmail(
                V2SemanticOperation.Summarize,
                ordinal: 1,
                reference: V2SemanticReference.LatestProviderResult),
            V2GmailTools.SummarizeThread
        },
        {
            "Find Acme.",
            Salesforce(V2SemanticOperation.Search, entity: "account", searchText: "Acme"),
            V2SalesforceTools.SearchRecords
        },
        {
            "Show its open opportunities.",
            Salesforce(
                V2SemanticOperation.Related,
                entity: "opportunity",
                reference: V2SemanticReference.LatestProviderResult,
                filters: [new("open", V2SemanticFilterOperator.Equals, "true")]),
            V2SalesforceTools.ReadRecords
        },
        {
            "Only those closing this qarter.",
            Salesforce(
                V2SemanticOperation.Refine,
                entity: "opportunity",
                reference: V2SemanticReference.LatestProviderResult,
                timeRange: V2SemanticTimeRange.CurrentQuarter),
            V2SalesforceTools.ReadRecords
        },
        {
            "Sort those by amount.",
            Salesforce(
                V2SemanticOperation.Refine,
                entity: "opportunity",
                reference: V2SemanticReference.LatestProviderResult,
                sorts: [new("amount", V2SemanticSortDirection.Descending)]),
            V2SalesforceTools.ReadRecords
        },
        {
            "Show the next page.",
            Salesforce(
                V2SemanticOperation.NextPage,
                reference: V2SemanticReference.LatestProviderResult),
            V2SalesforceTools.ContinueRecords
        },
        {
            "Total open pipeline by owner this quarter.",
            Salesforce(
                V2SemanticOperation.Aggregate,
                entity: "opportunity",
                filters: [new("open", V2SemanticFilterOperator.Equals, "true")],
                aggregate: new(V2SemanticAggregateFunction.Sum, "amount", "owner"),
                timeRange: V2SemanticTimeRange.CurrentQuarter),
            V2SalesforceTools.AggregateRecords
        },
        {
            "Search accounts, contacts, leads, and warranty claims for Acme.",
            Salesforce(V2SemanticOperation.Search, entity: "all accessible", searchText: "Acme"),
            V2SalesforceTools.SearchRecords
        },
        {
            "What Salesforce objects can I use?",
            Salesforce(V2SemanticOperation.Discover),
            V2SalesforceTools.DiscoverObjects
        },
        {
            "Find the Salesforce account matching the sender of my latest email.",
            new(
                V2SemanticProvider.CrossProvider,
                V2SemanticOperation.Match,
                Entity: "account",
                Reference: V2SemanticReference.LatestGmailSender),
            V2CrossProviderTools.MatchSalesforceAccountToGmailSender
        },
        {
            "Set Acme's rating to Hot.",
            Salesforce(
                V2SemanticOperation.MutationPreview,
                entity: "account",
                searchText: "Acme",
                filters: [new("rating", V2SemanticFilterOperator.Set, "Hot")]),
            V2SalesforceTools.PreviewMutation
        }
    };

    [Theory]
    [MemberData(nameof(DirectReadCases))]
    public async Task Semantic_proposals_compile_to_closed_typed_tools(
        string prompt,
        V2SemanticIntentProposal proposal,
        string expectedTool)
    {
        var resolver = new StubResolver(proposal);
        var planner = new V2McpIntegrationPlanner(resolver);

        var invocation = Assert.Single(await planner.PlanAsync(Request(prompt)));

        Assert.Equal(expectedTool, invocation.ToolId);
        Assert.DoesNotContain(
            EnumeratePropertyNames(invocation.Input),
            name => ForbiddenProviderInputNames.Contains(Normalize(name)));
        Assert.Equal(prompt, Assert.Single(resolver.Requests).Prompt);
    }

    [Fact]
    public async Task Ambiguous_provider_reference_clarifies_without_a_provider_call()
    {
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.Ambiguous,
            V2SemanticOperation.Clarify,
            Clarification: "Do you mean Gmail or Salesforce?");
        var invocation = Assert.Single(await new V2McpIntegrationPlanner(new StubResolver(proposal))
            .PlanAsync(Request("Show me the second one.")));

        Assert.Equal(V2AssistantTools.Clarify, invocation.ToolId);
        Assert.Equal("Do you mean Gmail or Salesforce?", invocation.Input.GetProperty("message").GetString());
    }

    [Fact]
    public async Task General_conversation_does_not_invoke_a_provider_tool()
    {
        var proposal = new V2SemanticIntentProposal(
            V2SemanticProvider.None,
            V2SemanticOperation.Answer);

        var invocations = await new V2McpIntegrationPlanner(new StubResolver(proposal))
            .PlanAsync(Request("Help me think through this decision."));

        Assert.Empty(invocations);
    }

    [Fact]
    public async Task Raw_query_and_delete_proposals_fail_closed()
    {
        var rawQuery = Salesforce(
            V2SemanticOperation.QueryLanguage,
            entity: "Account",
            searchText: "SELECT Id FROM Account");
        var delete = Salesforce(
            V2SemanticOperation.Delete,
            entity: "account",
            searchText: "Acme");

        var queryInvocation = Assert.Single(await new V2McpIntegrationPlanner(new StubResolver(rawQuery))
            .PlanAsync(Request("Run this SOQL.")));
        var deleteInvocation = Assert.Single(await new V2McpIntegrationPlanner(new StubResolver(delete))
            .PlanAsync(Request("Delete Acme.")));

        Assert.Equal(V2AssistantTools.Clarify, queryInvocation.ToolId);
        Assert.Equal(V2AssistantTools.Clarify, deleteInvocation.ToolId);
    }

    private static V2SemanticIntentProposal Gmail(
        V2SemanticOperation operation,
        int limit = 1,
        int? ordinal = null,
        V2SemanticReference reference = V2SemanticReference.None,
        IReadOnlyList<V2SemanticFilter>? filters = null,
        IReadOnlyList<V2SemanticSort>? sorts = null,
        V2SemanticTimeRange timeRange = V2SemanticTimeRange.None) =>
        new(
            V2SemanticProvider.Gmail,
            operation,
            Limit: limit,
            Ordinal: ordinal,
            Reference: reference,
            Filters: filters,
            Sorts: sorts,
            TimeRange: timeRange);

    private static V2SemanticIntentProposal Salesforce(
        V2SemanticOperation operation,
        string? entity = null,
        int limit = 10,
        int? ordinal = null,
        V2SemanticReference reference = V2SemanticReference.None,
        IReadOnlyList<V2SemanticFilter>? filters = null,
        IReadOnlyList<V2SemanticSort>? sorts = null,
        V2SemanticAggregate? aggregate = null,
        V2SemanticTimeRange timeRange = V2SemanticTimeRange.None,
        string? searchText = null) =>
        new(
            V2SemanticProvider.Salesforce,
            operation,
            Entity: entity,
            Limit: limit,
            Ordinal: ordinal,
            Reference: reference,
            Filters: filters,
            Sorts: sorts,
            Aggregate: aggregate,
            TimeRange: timeRange,
            SearchText: searchText);

    private static V2ConversationRequest Request(string text) =>
        new(
            new V2RequestContext(
                new TenantId("tenant"),
                new WorkspaceId("workspace"),
                new PrincipalRef("user", PrincipalKind.User),
                "session",
                AuthAssurance.Password,
                "correlation",
                null,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "gmail.read",
                    "salesforce.read",
                    "brain.act",
                    "ui.action"
                }),
            "conversation",
            text);

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                yield return property.Name;
                foreach (var nested in EnumeratePropertyNames(property.Value))
                    yield return nested;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                foreach (var nested in EnumeratePropertyNames(item))
                    yield return nested;
            }
        }
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static readonly HashSet<string> ForbiddenProviderInputNames = new(StringComparer.Ordinal)
    {
        "query",
        "gmailquery",
        "soql",
        "sosl",
        "url",
        "nextrecordsurl",
        "objectname",
        "fieldname",
        "mutationpayload"
    };

    private sealed class StubResolver(V2SemanticIntentProposal proposal) : IV2SemanticIntentResolver
    {
        public List<V2SemanticIntentRequest> Requests { get; } = [];

        public Task<V2SemanticIntentProposal> ResolveAsync(
            V2SemanticIntentRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(proposal);
        }
    }
}
