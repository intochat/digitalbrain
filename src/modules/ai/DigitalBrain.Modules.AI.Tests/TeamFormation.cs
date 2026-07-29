using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class TeamFormation(ModuleFixture fixture)
{
    private const string Gemma = "Gemma4";
    private const string Llama = "Llama32";
    private const string Qwen = "Qwen35";
    private const string Granite = "Granite41";
    private const string Gpt = "Gpt56";
    private const string GemmaReply = "gemma-reply";
    private const string LlamaReply = "llama-reply";
    private const string QwenReply = "qwen-reply";
    private const string GraniteReply = "granite-reply";
    private const string Prompt = "compare these two";
    private const string ComparisonTeam = "compare-team";
    private const string CorrectedTeam = "corrected-team";
    private const int Pair = 2;

    [Fact(DisplayName = "a team formed with two model names streams back a reply from both models")]
    public async Task FormedTeamStreamsBackEveryModelsReply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);

        var team = test.Client.Get<ITeam>(ComparisonTeam);
        await team.Form(Formation(Gemma, Llama));

        var streamed = await StreamAsync(team, cancellationToken);

        Assert.Contains(GemmaReply, streamed, StringComparison.Ordinal);
        Assert.Contains(LlamaReply, streamed, StringComparison.Ordinal);
        Assert.Equal(Pair, test.Chat().CallCount);
        await AssertRespondedAsync(test.Neuron<IGemma4>(ComparisonTeam), 1, cancellationToken);
        await AssertRespondedAsync(test.Neuron<ILlama32>(ComparisonTeam), 1, cancellationToken);
    }

    [Fact(DisplayName = "re-forming a team that has never responded replaces the line-up that then runs")]
    public async Task ReFormingBeforeTheTeamRespondsReplacesTheLineUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test, QwenReply, GraniteReply);

        var team = test.Client.Get<ITeam>(CorrectedTeam);
        await team.Form(Formation(Gemma, Llama));
        await team.Form(Formation(Qwen, Granite));

        var streamed = await StreamAsync(team, cancellationToken);

        Assert.Contains(QwenReply, streamed, StringComparison.Ordinal);
        Assert.Contains(GraniteReply, streamed, StringComparison.Ordinal);
        await AssertRespondedAsync(test.Neuron<IQwen35>(CorrectedTeam), 1, cancellationToken);
        await AssertRespondedAsync(test.Neuron<IGranite41>(CorrectedTeam), 1, cancellationToken);
        await AssertRespondedAsync(test.Neuron<IGemma4>(CorrectedTeam), 0, cancellationToken);
        await AssertRespondedAsync(test.Neuron<ILlama32>(CorrectedTeam), 0, cancellationToken);
    }

    [Fact(DisplayName = "re-forming a responded team with the same models leaves the line-up and its durable session intact")]
    public async Task FormingTheSameTeamTwiceIsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);
        ScriptPair(test);

        var team = test.Client.Get<ITeam>("idempotent-team");
        await team.Form(Formation(Gemma, Llama));
        var first = await StreamAsync(team, cancellationToken);

        await team.Form(Formation(Gemma.ToUpperInvariant(), Llama.ToUpperInvariant()));

        var second = await StreamAsync(team, cancellationToken);

        Assert.Contains(GemmaReply, first, StringComparison.Ordinal);
        Assert.Contains(LlamaReply, first, StringComparison.Ordinal);
        Assert.Contains(GemmaReply, second, StringComparison.Ordinal);
        Assert.Contains(LlamaReply, second, StringComparison.Ordinal);
        Assert.Equal(Pair * 2, test.Chat().CallCount);
    }

    [Fact(DisplayName = "a line-up naming a model this deployment cannot run throws instead of answering nothing")]
    public async Task AnUnreachableLineUpThrowsNamingTheParticipantsItCouldNotReach()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var team = test.Client.Get<ITeam>("unprovisioned-team");
        await team.Form(Formation(Gpt, Gemma));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StreamAsync(team, cancellationToken));

        Assert.Contains($"Failed to run: {Gpt} ", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"Never given a turn: {Gemma} ", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(failure.InnerException);
        Assert.Equal(0, test.Chat().CallCount);
    }

    [Fact(DisplayName = "a respond that reached nobody does not freeze the line-up, so the team is still correctable")]
    public async Task AFailedRespondLeavesTheLineUpCorrectable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);

        var team = test.Client.Get<ITeam>("recovered-team");
        await team.Form(Formation(Gpt, Gemma));

        await Assert.ThrowsAsync<InvalidOperationException>(() => StreamAsync(team, cancellationToken));

        await team.Form(Formation(Gemma, Llama));

        var streamed = await StreamAsync(team, cancellationToken);

        Assert.Contains(GemmaReply, streamed, StringComparison.Ordinal);
        Assert.Contains(LlamaReply, streamed, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "forming a responded team with different models throws and names both line-ups")]
    public async Task FormingDifferentModelsAfterTheTeamRespondedThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);

        var team = test.Client.Get<ITeam>("re-formed-team");
        await team.Form(Formation(Gemma, Llama));
        await StreamAsync(team, cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => team.Form(Formation(Qwen, Granite)));

        Assert.Contains(Gemma, failure.Message, StringComparison.Ordinal);
        Assert.Contains(Llama, failure.Message, StringComparison.Ordinal);
        Assert.Contains(Qwen, failure.Message, StringComparison.Ordinal);
        Assert.Contains(Granite, failure.Message, StringComparison.Ordinal);
        Assert.Equal(Pair, test.Chat().CallCount);
    }

    [Fact(DisplayName = "responding before the team is formed throws naming the team and the call that forms it")]
    public async Task RespondingBeforeTheTeamIsFormedThrows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => test.Client.Get<ITeam>("unformed-team").Respond([new ChatMessage(ChatRole.User, Prompt)]));

        Assert.Contains("unformed-team", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ITeam.Form), failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, test.Chat().CallCount);
    }

    [Fact(DisplayName = "an unknown model is named, the known models are listed, and provisioning is not promised")]
    public async Task FormingWithAnUnknownModelNamesItAndListsTheKnownModelsWithoutPromisingThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => test.Client.Get<ITeam>("unknown-model-team").Form(Formation(Gemma, "Gemini9")));

        Assert.Contains("Gemini9", failure.Message, StringComparison.Ordinal);
        Assert.All(
            new[] { Gemma, Llama, Qwen, Granite, Gpt },
            model => Assert.Contains(model, failure.Message, StringComparison.Ordinal));
        Assert.Contains("provisioned", failure.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "forming a team that names one model twice is rejected before any model runs")]
    public async Task FormingTheSameModelTwiceIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<ArgumentException>(
            () => test.Client.Get<ITeam>("duplicate-model-team")
                .Form(Formation(Gemma, Gemma.ToUpperInvariant())));

        Assert.Contains(Gemma, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, test.Chat().CallCount);
    }

    [Fact(DisplayName = "a line-up frozen by responding outlives a host restart and still refuses a different one")]
    public async Task FrozenLineUpOutlivesAHostRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);
        ScriptPair(test);

        var team = test.Neuron<ITeam>("restarted-team");
        await team.Reference.Form(Formation(Gemma, Llama));
        await StreamAsync(team.Reference, cancellationToken);

        await team.RestartHostAsync(cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => team.Reference.Form(Formation(Qwen, Granite)));

        var streamed = await StreamAsync(team.Reference, cancellationToken);

        Assert.Contains(GemmaReply, streamed, StringComparison.Ordinal);
        Assert.Contains(LlamaReply, streamed, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a participant built from a runtime model type describes identically to the compile-time one")]
    public void RuntimeBuiltParticipantDescribesIdenticallyToTheCompileTimeEquivalent()
    {
        var id = NeuronId.For<IGemma4>(new OwnerId("fingerprint-owner"), "compare-team");

        Assert.Equal(
            MafParticipantAdapter.Describe(new Participant<IGemma4>(id)),
            MafParticipantAdapter.Describe(Participant.Of(typeof(IGemma4), id)));
    }

    private static DigitalBrain.AI.TeamFormation Formation(params string[] models) => new(models);

    private static void ScriptPair(TestBrain test) => ScriptPair(test, GemmaReply, LlamaReply);

    private static void ScriptPair(TestBrain test, string first, string second)
    {
        test.Chat().Reply(first);
        test.Chat().Reply(second);
    }

    private static async Task AssertRespondedAsync<TModel>(
        TestNeuron<TModel> model,
        int expected,
        CancellationToken cancellationToken)
        where TModel : class, ILLM
        => Assert.Equal(
            expected,
            (await model.Incoming.ReadAsync<CapabilityRequested>(
                afterSequence: 0, cancellationToken: cancellationToken))
            .Count(request => request.Synapse.Method == nameof(ILLM.RespondStreaming)));

    private static async Task<string> StreamAsync(ITeam team, CancellationToken cancellationToken)
    {
        var streamed = new StringBuilder();

        await foreach (var update in team.RespondStreaming(
            [new ChatMessage(ChatRole.User, Prompt)], cancellationToken))
        {
            streamed.Append(update.Text);
        }

        return streamed.ToString();
    }
}
