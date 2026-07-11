extern alias McpProject;

using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;
using ISemanticIntentResolver = McpProject::DigitalBrain.Mcp.ISemanticIntentResolver;
using McpIntegrationPlanner = McpProject::DigitalBrain.Mcp.McpIntegrationPlanner;

namespace DigitalBrain.Tests.Runtime;

public sealed class ConversationalCapabilityBenchmarkTests
{
    public static TheoryData<string, SemanticIntentProposal, string> DirectReadCases => new()
    {
        {
            "Get my last 2 incoming emails.",
            Gmail(SemanticOperation.List, limit: 2),
            GmailTools.ReadMessages
        },
        {
            "gimme my two newest mails pls",
            Gmail(SemanticOperation.List, limit: 2),
            GmailTools.ReadMessages
        },
        {
            "Only unread.",
            Gmail(
                SemanticOperation.Refine,
                reference: SemanticReference.LatestProviderResult,
                filters: [new("read state", SemanticFilterOperator.Equals, "unread")]),
            GmailTools.ReadMessages
        },
        {
            "Now the one before those.",
            Gmail(
                SemanticOperation.Previous,
                ordinal: 1,
                reference: SemanticReference.LatestProviderResult),
            GmailTools.ReadMessages
        },
        {
            "Show emails from that sender last week.",
            Gmail(
                SemanticOperation.Refine,
                reference: SemanticReference.SameSender,
                timeRange: SemanticTimeRange.PreviousWeek),
            GmailTools.ReadMessages
        },
        {
            "Which have attachments?",
            Gmail(
                SemanticOperation.Refine,
                reference: SemanticReference.LatestProviderResult,
                filters: [new("attachment", SemanticFilterOperator.Equals, "present")]),
            GmailTools.ReadMessages
        },
        {
            "Give me an inbox and unread overview.",
            Gmail(SemanticOperation.Overview),
            GmailTools.ReadMailboxOverview
        },
        {
            "Find the first matching thread.",
            Gmail(SemanticOperation.Threads, limit: 1),
            GmailTools.ReadThreads
        },
        {
            "sumarize the first thread",
            Gmail(
                SemanticOperation.Summarize,
                ordinal: 1,
                reference: SemanticReference.LatestProviderResult),
            GmailTools.SummarizeThread
        },
        {
            "Find Acme.",
            Salesforce(SemanticOperation.Search, entity: "account", searchText: "Acme"),
            SalesforceTools.SearchRecords
        },
        {
            "Show its open opportunities.",
            Salesforce(
                SemanticOperation.Related,
                entity: "opportunity",
                reference: SemanticReference.LatestProviderResult,
                filters: [new("open", SemanticFilterOperator.Equals, "true")]),
            SalesforceTools.ReadRecords
        },
        {
            "Only those closing this qarter.",
            Salesforce(
                SemanticOperation.Refine,
                entity: "opportunity",
                reference: SemanticReference.LatestProviderResult,
                timeRange: SemanticTimeRange.CurrentQuarter),
            SalesforceTools.ReadRecords
        },
        {
            "Sort those by amount.",
            Salesforce(
                SemanticOperation.Refine,
                entity: "opportunity",
                reference: SemanticReference.LatestProviderResult,
                sorts: [new("amount", SemanticSortDirection.Descending)]),
            SalesforceTools.ReadRecords
        },
        {
            "Show the next page.",
            Salesforce(
                SemanticOperation.NextPage,
                reference: SemanticReference.LatestProviderResult),
            SalesforceTools.ContinueRecords
        },
        {
            "Total open pipeline by owner this quarter.",
            Salesforce(
                SemanticOperation.Aggregate,
                entity: "opportunity",
                filters: [new("open", SemanticFilterOperator.Equals, "true")],
                aggregate: new(SemanticAggregateFunction.Sum, "amount", "owner"),
                timeRange: SemanticTimeRange.CurrentQuarter),
            SalesforceTools.AggregateRecords
        },
        {
            "Search accounts, contacts, leads, and warranty claims for Acme.",
            Salesforce(SemanticOperation.Search, entity: "all accessible", searchText: "Acme"),
            SalesforceTools.SearchRecords
        },
        {
            "What Salesforce objects can I use?",
            Salesforce(SemanticOperation.Discover),
            SalesforceTools.DiscoverObjects
        },
        {
            "Find the Salesforce account matching the sender of my latest email.",
            new(
                SemanticProvider.CrossProvider,
                SemanticOperation.Match,
                Entity: "account",
                Reference: SemanticReference.LatestGmailSender),
            CrossProviderTools.MatchSalesforceAccountToGmailSender
        },
        {
            "Set Acme's rating to Hot.",
            Salesforce(
                SemanticOperation.MutationPreview,
                entity: "account",
                searchText: "Acme",
                filters: [new("rating", SemanticFilterOperator.Set, "Hot")]),
            SalesforceTools.PreviewMutation
        }
    };

    [Theory]
    [MemberData(nameof(DirectReadCases))]
    public async Task Semantic_proposals_compile_to_closed_typed_tools(
        string prompt,
        SemanticIntentProposal proposal,
        string expectedTool)
    {
        var resolver = new StubResolver(proposal);
        var planner = new McpIntegrationPlanner(resolver);

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
        var proposal = new SemanticIntentProposal(
            SemanticProvider.Ambiguous,
            SemanticOperation.Clarify,
            Clarification: "Do you mean Gmail or Salesforce?");
        var invocation = Assert.Single(await new McpIntegrationPlanner(new StubResolver(proposal))
            .PlanAsync(Request("Show me the second one.")));

        Assert.Equal(AssistantTools.Clarify, invocation.ToolId);
        Assert.Equal("Do you mean Gmail or Salesforce?", invocation.Input.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("Tell me how my current Salesforce works.")]
    [InlineData("How does my Salesforce work?")]
    [InlineData("What can I do with Salesforce?")]
    [InlineData("Salesforce capabilities")]
    public async Task Salesforce_capability_help_is_deterministic_without_semantic_model(string prompt)
    {
        var resolver = new StubResolver(Salesforce(SemanticOperation.Search, searchText: "must-not-run"));
        var invocation = Assert.Single(await new McpIntegrationPlanner(resolver).PlanAsync(Request(prompt)));

        Assert.Equal(AssistantTools.Clarify, invocation.ToolId);
        Assert.Equal(
            "I can safely discover and search Salesforce objects, read details and related records, aggregate, sort, and page results. Ask for a specific account, opportunity, or object; if Salesforce isn’t connected, I’ll ask you to connect it first.",
            invocation.Input.GetProperty("message").GetString());
        Assert.Empty(resolver.Requests);
    }

    [Theory]
    [InlineData(SemanticProvider.Gmail, "What should I look up in Gmail?")]
    [InlineData(SemanticProvider.Salesforce, "What should I look up in Salesforce?")]
    [InlineData(SemanticProvider.CrossProvider, "What should I match between Gmail and Salesforce?")]
    [InlineData(SemanticProvider.None, "What should I look up, and in which connected service: Gmail or Salesforce?")]
    public async Task Model_generated_clarification_is_never_forwarded_to_the_user(
        SemanticProvider provider,
        string expectedMessage)
    {
        const string untrustedClarification =
            "Open https://internal.example/oauth?contextId=ctx-secret and include the internal context ID.";
        var proposal = new SemanticIntentProposal(
            provider,
            SemanticOperation.Clarify,
            Clarification: untrustedClarification);

        var invocation = Assert.Single(await new McpIntegrationPlanner(new StubResolver(proposal))
            .PlanAsync(Request("Find the relevant Salesforce record.")));
        var message = Assert.IsType<string>(invocation.Input.GetProperty("message").GetString());

        Assert.Equal(AssistantTools.Clarify, invocation.ToolId);
        Assert.Equal(expectedMessage, message);
        Assert.DoesNotContain("internal.example", message);
        Assert.DoesNotContain("ctx-secret", message);
    }

    [Fact]
    public async Task General_conversation_does_not_invoke_a_provider_tool()
    {
        var proposal = new SemanticIntentProposal(
            SemanticProvider.None,
            SemanticOperation.Answer);

        var invocations = await new McpIntegrationPlanner(new StubResolver(proposal))
            .PlanAsync(Request("Help me think through this decision."));

        Assert.Empty(invocations);
    }

    [Fact]
    public async Task Raw_query_and_delete_proposals_fail_closed()
    {
        var rawQuery = Salesforce(
            SemanticOperation.QueryLanguage,
            entity: "Account",
            searchText: "SELECT Id FROM Account");
        var delete = Salesforce(
            SemanticOperation.Delete,
            entity: "account",
            searchText: "Acme");

        var queryInvocation = Assert.Single(await new McpIntegrationPlanner(new StubResolver(rawQuery))
            .PlanAsync(Request("Run this SOQL.")));
        var deleteInvocation = Assert.Single(await new McpIntegrationPlanner(new StubResolver(delete))
            .PlanAsync(Request("Delete Acme.")));

        Assert.Equal(AssistantTools.Clarify, queryInvocation.ToolId);
        Assert.Equal(AssistantTools.Clarify, deleteInvocation.ToolId);
    }

    private static SemanticIntentProposal Gmail(
        SemanticOperation operation,
        int limit = 1,
        int? ordinal = null,
        SemanticReference reference = SemanticReference.None,
        IReadOnlyList<SemanticFilter>? filters = null,
        IReadOnlyList<SemanticSort>? sorts = null,
        SemanticTimeRange timeRange = SemanticTimeRange.None) =>
        new(
            SemanticProvider.Gmail,
            operation,
            Limit: limit,
            Ordinal: ordinal,
            Reference: reference,
            Filters: filters,
            Sorts: sorts,
            TimeRange: timeRange);

    private static SemanticIntentProposal Salesforce(
        SemanticOperation operation,
        string? entity = null,
        int limit = 10,
        int? ordinal = null,
        SemanticReference reference = SemanticReference.None,
        IReadOnlyList<SemanticFilter>? filters = null,
        IReadOnlyList<SemanticSort>? sorts = null,
        SemanticAggregate? aggregate = null,
        SemanticTimeRange timeRange = SemanticTimeRange.None,
        string? searchText = null) =>
        new(
            SemanticProvider.Salesforce,
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

    private static ConversationRequest Request(string text) =>
        new(
            new RuntimeRequestContext(
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

    private sealed class StubResolver(SemanticIntentProposal proposal) : ISemanticIntentResolver
    {
        public List<SemanticIntentRequest> Requests { get; } = [];

        public Task<SemanticIntentProposal> ResolveAsync(
            SemanticIntentRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(proposal);
        }
    }
}
