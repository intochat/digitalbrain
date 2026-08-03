using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class BehaviorAuthoring(BehaviorAuthoringFixture fixture)
{
    private const string CurrentProgram = "public sealed class Program {}";

    private const string ModelGeneratedProgram =
        """
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record DemoTrigger() : Synapse;

        public sealed class DemoProgram : IBehaviorProgram<DemoTrigger>
        {
            public ValueTask ExecuteAsync(DemoTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                => ValueTask.CompletedTask;
        }
        """;

    [Fact(DisplayName =
        "a drafting request gives up inside one outbox delivery attempt rather than throwing into the retry horizon")]
    public void DraftingGivesUpInsideOneDeliveryAttempt()
    {
        // Strictly less, not equal: TryDeliverAsync arms the outer attemptCts.CancelAfter
        // (DeliveryAttemptTimeout) before Deliver even starts, so a bound equal to that outer
        // deadline always loses the race to it - ProposeAsync would see OperationCanceledException,
        // never the TimeoutException its refusal branch exists to catch. This mechanism is proven
        // reachable by IntrospectionDirect.InnerReadBoundWinsTheRaceAgainstTheOuterDeliveryDeadline,
        // which races the identical DeliveryPolicy.InnerDeliveryReadBound this bound is drawn from.
        Assert.True(
            BehaviorAuthorNeuron.BehaviorReadBound < DeliveryPolicy.DeliveryAttemptTimeout,
            $"A read bound of {BehaviorAuthorNeuron.BehaviorReadBound} does not come in strictly "
            + $"under the outer {DeliveryPolicy.DeliveryAttemptTimeout} delivery-attempt deadline "
            + "armed before this handler's turn starts, so the timeout catch can never win that race.");
    }

    [Fact(DisplayName =
        "drafting resolves its author through the DI-fallback lambda when no IBehaviorAuthor is registered, without the fallback ever calling the model")]
    public async Task ProposeResolvesTheAuthorFallbackWhenNoAuthorIsRegistered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string behaviorId = "com.demo.fallback";
        const string requestText = "also enrich phone numbers";

        // BehaviorAuthoringFixture registers no IBehaviorAuthor, so BehaviorAuthorNeuron.Author()
        // must resolve through its fallback lambda over IGemma4 to serve this request at all.
        // Propose only calls BehaviorAuthor.ProposeScenarios, which never calls the model (see
        // ApplyApprovedScenariosEmitsModelGeneratedProgram for the call that does), so this proves the
        // fallback's construction and DI resolution compile and wire correctly without scripting Gemma4.
        var proposed = await test.Client.GetGrainProxy<IBehaviorAuthoring>()
            .Propose(new ProposeBehaviorChangeRequest(behaviorId, requestText));

        Assert.True(proposed.Succeeded);
        Assert.NotNull(proposed.Proposal);
        Assert.Equal(behaviorId, proposed.Proposal.BehaviorId);
        Assert.Equal(BehaviorChangeStatus.AwaitingScenarioApproval, proposed.Proposal.Status);
        Assert.Contains($"Scenario: {requestText}", proposed.Proposal.ProposedFeatureText, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a drafting request refuses an unaddressable behavior identity before it is delivered")]
    public void DraftingRefusesAnUnaddressableBehaviorIdentity()
    {
        var unaddressable = Assert.Throws<ArgumentException>(
            () => new ProposeBehaviorChangeRequest("other-owner/enrichment", "also enrich phone numbers"));
        Assert.Contains("not addressable", unaddressable.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(
            () => new ProposeBehaviorChangeRequest("enrichment", "   "));
    }

    [Fact(DisplayName =
        "every durable authoring map is bounded, the model-callable drafting map included")]
    public void EveryDurableAuthoringMapIsBounded()
    {
        var maps = typeof(BehaviorAuthorNeuron.AuthoringData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotEmpty(maps);
        Assert.All(maps, map => Assert.Equal(
            typeof(BoundedLedger<,>),
            map.PropertyType.IsGenericType ? map.PropertyType.GetGenericTypeDefinition() : map.PropertyType));
    }

    [Fact(DisplayName =
        "a bounded ledger evicts the oldest entry, and still does after an entry has been settled out of it")]
    public void BoundedLedgerEvictsOldestFirst()
    {
        const int Bound = 4;
        var ledger = new BoundedLedger<string, string>();
        for (var entry = 0; entry < Bound; entry++)
        {
            ledger = ledger.With($"key-{entry}", $"value-{entry}", Bound);
        }

        // Frees a slot a Dictionary reuses out of insertion order, which is what makes "the first
        // key" the wrong entry to evict from that point on.
        ledger = ledger.Without("key-1");
        ledger = ledger.With("key-4", "value-4", Bound);
        ledger = ledger.With("key-5", "value-5", Bound);

        Assert.Equal(["key-2", "key-3", "key-4", "key-5"], ledger.Order);
        Assert.False(ledger.TryGet("key-0", out _));
        Assert.True(ledger.TryGet("key-5", out var newest));
        Assert.Equal("value-5", newest);
    }

    [Fact(DisplayName = "re-adding a key refreshes its place in the ledger instead of duplicating it")]
    public void BoundedLedgerRefreshesRatherThanDuplicates()
    {
        const int Bound = 2;
        var ledger = new BoundedLedger<string, string>()
            .With("first", "one", Bound)
            .With("second", "two", Bound)
            .With("first", "one-again", Bound)
            .With("third", "three", Bound);

        Assert.Equal(["first", "third"], ledger.Order);
        Assert.True(ledger.TryGet("first", out var refreshed));
        Assert.Equal("one-again", refreshed);
        Assert.False(ledger.TryGet("second", out _));
    }

    [Fact(DisplayName = "natural-language request returns a feature/scenario diff before source changes")]
    public void ProposeScenariosReturnsFeatureDiffWithoutCode()
    {
        using var chat = new ScriptedChatClient();
        var author = BehaviorAuthor.ForChatClient(chat);
        var proposal = author.ProposeScenarios(new BehaviorChangeRequest(
            BehaviorId: "com.demo",
            RequestText: "also enrich phone numbers",
            CurrentFeatureText: "Feature: demo\n  Scenario: base\n",
            CurrentProgramSource: CurrentProgram,
            DisplayName: "Demo",
            FeatureName: "install"));

        Assert.Contains("Scenario: also enrich phone numbers", proposal.ProposedFeatureText, StringComparison.Ordinal);
        Assert.DoesNotContain("class ", proposal.ProposedFeatureText, StringComparison.Ordinal);
        Assert.True(proposal.RequiresApproval);
        Assert.Contains("before any source generation", proposal.DiffSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "approved scenarios emit model-generated C# program, not current source passthrough")]
    public async Task ApplyApprovedScenariosEmitsModelGeneratedProgram()
    {
        using var chat = new ScriptedChatClient();
        chat.Reply(ModelGeneratedProgram);
        var author = BehaviorAuthor.ForChatClient(chat);
        var request = new BehaviorChangeRequest(
            "com.demo",
            "also enrich phone numbers",
            "Feature: demo\n",
            CurrentProgram,
            "Demo",
            "install");
        var proposal = author.ProposeScenarios(request);

        var result = await author.ApplyApprovedScenarios(request, proposal, TestContext.Current.CancellationToken);

        Assert.True(result.ReadyForPropose);
        Assert.Equal(proposal.ProposedFeatureText, result.FeatureText);
        Assert.Equal(ModelGeneratedProgram.Trim(), result.ProgramSource.Trim());
        Assert.NotEqual(request.CurrentProgramSource, result.ProgramSource);
        Assert.Equal(1, chat.CallCount);
        Assert.Contains(
            request.RequestText,
            string.Join('\n', chat.LastMessages.Select(static message => message.Text)),
            StringComparison.Ordinal);
        Assert.Contains(
            request.CurrentProgramSource,
            string.Join('\n', chat.LastMessages.Select(static message => message.Text)),
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "approved scenarios strip markdown fences from model program replies")]
    public async Task ApplyApprovedScenariosStripsMarkdownFences()
    {
        using var chat = new ScriptedChatClient();
        chat.Reply(
            """
            ```csharp
            public sealed class GeneratedProgram {}
            ```
            """);
        var author = BehaviorAuthor.ForChatClient(chat);
        var request = new BehaviorChangeRequest(
            "com.demo",
            "add a binding",
            "Feature: demo\n",
            CurrentProgram,
            "Demo",
            "install");
        var proposal = author.ProposeScenarios(request);

        var result = await author.ApplyApprovedScenarios(request, proposal, TestContext.Current.CancellationToken);

        Assert.Equal("public sealed class GeneratedProgram {}", result.ProgramSource.Trim());
        Assert.DoesNotContain("```", result.ProgramSource, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "approved scenarios stay propose-ready without auto-publishing")]
    public async Task ApplyApprovedScenariosIsProposeReadyOnly()
    {
        using var chat = new ScriptedChatClient();
        chat.Reply(ModelGeneratedProgram);
        var author = BehaviorAuthor.ForChatClient(chat);
        var request = new BehaviorChangeRequest(
            "com.demo",
            "also enrich phone numbers",
            "Feature: demo\n",
            CurrentProgram,
            "Demo",
            "install");
        var proposal = author.ProposeScenarios(request);

        var result = await author.ApplyApprovedScenarios(request, proposal, TestContext.Current.CancellationToken);

        Assert.True(result.ReadyForPropose);
        Assert.Equal(proposal.ProposedFeatureText, result.FeatureText);
        Assert.Equal("install", result.FeatureName);
    }
}
