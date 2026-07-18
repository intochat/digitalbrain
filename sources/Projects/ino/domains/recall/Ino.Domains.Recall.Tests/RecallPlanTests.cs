using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Recall.Contracts;
using Ino.Domains.Recall.Plans;
using NSubstitute;
using Xunit;

namespace Ino.Domains.Recall.Tests;

/// <summary>
/// Slice C: <see cref="RecallPlan"/> static-body tests. The plan strips the
/// recall verb and fires <see cref="RecallQuestion"/> via the traversal
/// engine; tests substitute the engine and assert the synapse content.
///
/// Neuron-level integration ("RecallNeuron handles RecallQuestion against a
/// real Qdrant collection") needs a live Qdrant test container and lives
/// outside this project — tracked in docs/plan-poc-phase-4.md Slice C.
/// </summary>
public sealed class RecallPlanTests
{
    [Theory]
    [InlineData("what did I tell you about my mum's birthday", "my mum's birthday")]
    [InlineData("do you remember where I parked the car", "where I parked the car")]
    [InlineData("recall my favourite colour", "my favourite colour")]
    [InlineData("what did I say about lisbon", "lisbon")]
    [InlineData("Do you remember the password I set", "the password I set")]
    public async Task Strips_recall_prefix_and_fires_residual_question(string prompt, string expectedQuestion)
    {
        var engine = Substitute.For<ITraversalEngine>();
        RecallQuestion? captured = null;
        engine.FireAsync(Arg.Do<RecallQuestion>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("hit-narrated"));

        var result = await RecallPlan.ExecuteAsync(
            prompt: prompt,
            engine: engine,
            ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("hit-narrated", result.Message);
        Assert.NotNull(captured);
        Assert.Equal(expectedQuestion, captured!.Text);
    }

    [Fact]
    public async Task Prompt_with_no_known_prefix_passes_through_verbatim()
    {
        var engine = Substitute.For<ITraversalEngine>();
        RecallQuestion? captured = null;
        engine.FireAsync(Arg.Do<RecallQuestion>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok());

        await RecallPlan.ExecuteAsync(
            prompt: "the password I set last week",
            engine: engine,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("the password I set last week", captured!.Text);
    }

    [Fact]
    public void Stripping_an_empty_question_keeps_the_original_prompt()
    {
        // If the prompt is JUST the recall verb with no actual content, fall
        // back to the trimmed original so we don't fire an empty question.
        var stripped = RecallPlan.StripRecallPrefix("recall");
        Assert.Equal("recall", stripped);
    }
}
