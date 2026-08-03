using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class BehaviorAuthoring
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
        => Assert.Equal(DeliveryPolicy.DeliveryAttemptTimeout, BehaviorAuthorNeuron.BehaviorReadBound);

    [Fact(DisplayName = "a drafting request refuses an unaddressable behavior identity before it is delivered")]
    public void DraftingRefusesAnUnaddressableBehaviorIdentity()
    {
        var unaddressable = Assert.Throws<ArgumentException>(
            () => new ProposeBehaviorChangeRequest("other-owner/enrichment", "also enrich phone numbers"));
        Assert.Contains("not addressable", unaddressable.Message, StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(
            () => new ProposeBehaviorChangeRequest("enrichment", "   "));
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
