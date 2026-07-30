using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class AssistantModelTeam(OSBehaviorsFixture fixture)
{
    private const int FactTimeout = 120_000;
    private const string AssistantName = "assistant";
    private const string ConveneModelTeam = "convene_model_team";
    private const string EnrichAccountFromEmail = "enrich_account_from_email";
    private const string Gemma = "Gemma4";
    private const string Llama = "Llama32";
    private const string RecasedGemma = "GEMMA4";
    private const string RecasedLlama = "llama32";
    private const string ComparePrompt = "compare Gemma and Llama on this";
    private const string GreetingPrompt = "Hi";
    private const string TeamQuestion = "Which of you summarises better?";
    private const string GemmaVerdict = "gemma-verdict";
    private const string LlamaVerdict = "llama-verdict";
    private const string SecondGemmaVerdict = "gemma-second-verdict";
    private const string SecondLlamaVerdict = "llama-second-verdict";
    private const string FirstReply = "The two models disagree about summarising.";
    private const string SecondReply = "They still disagree.";
    private const string Greeting = "Hello! How can I help?";
    private const string PairedTeam = "team-Gemma4-Llama32";
    private const string ReversedTeam = "team-Llama32-Gemma4";
    private const string SoloTeam = "team-Gemma4";
    private const string UnknownModel = "Gemini9";
    private const string StorageOutage = "journal storage is offline for this test";
    private const string UndisclosedFailure = "Error: Function failed.";

    [Fact(Timeout = FactTimeout, DisplayName =
        "the assistant asked to compare two models convenes their team and the team's answer reaches its reply")]
    public async Task AssistantConvenesTheTeamAndItsAnswerReachesItsReply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptConvene(test, Gemma, Llama);
        ScriptTeamTurn(test, GemmaVerdict, LlamaVerdict);
        test.Chat().Reply(FirstReply);

        var response = await Assistant(test).Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);

        Assert.Equal(FirstReply, response.Text);
        Assert.Contains(
            ToolResultsOfLastCall(test),
            result => result.Contains(GemmaVerdict, StringComparison.Ordinal)
                && result.Contains(LlamaVerdict, StringComparison.Ordinal));
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(PairedTeam), 1, cancellationToken);
        await AssertTurnsTakenAsync(test.Neuron<ILlama32>(PairedTeam), 1, cancellationToken);
    }

    [Fact(DisplayName =
        "a line-up differing only in order and case is one team, and a different line-up is another")]
    public void OrderAndCaseCollapseIntoOneTeamWhileADifferentLineUpGetsItsOwn()
    {
        var declared = TeamLineUp.Of([Llama, Gemma]);
        var rephrased = TeamLineUp.Of([RecasedGemma, RecasedLlama]);

        Assert.Equal(PairedTeam, declared.TeamName);
        Assert.Equal(declared.TeamName, rephrased.TeamName);
        Assert.Equal(declared.Formation.Models, rephrased.Formation.Models);
        Assert.NotEqual(declared.TeamName, TeamLineUp.Of([Gemma, "Qwen35"]).TeamName);
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "the same two models named again in another order and case reach the team that already ran")]
    public async Task TheSameTwoModelsInAnotherOrderReachTheTeamThatAlreadyRan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptConvene(test, Llama, Gemma);
        ScriptTeamTurn(test, GemmaVerdict, LlamaVerdict);
        test.Chat().Reply(FirstReply);
        ScriptConvene(test, RecasedGemma, RecasedLlama);
        ScriptTeamTurn(test, SecondGemmaVerdict, SecondLlamaVerdict);
        test.Chat().Reply(SecondReply);

        var assistant = Assistant(test);
        var first = await assistant.Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);
        var second = await assistant.Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);

        Assert.Equal(FirstReply, first.Text);
        Assert.Equal(SecondReply, second.Text);
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(PairedTeam), 2, cancellationToken);
        await AssertTurnsTakenAsync(test.Neuron<ILlama32>(PairedTeam), 2, cancellationToken);
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(ReversedTeam), 0, cancellationToken);
        await AssertTurnsTakenAsync(test.Neuron<ILlama32>(ReversedTeam), 0, cancellationToken);
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "a model name this build does not know comes back to the model as a correctable message naming the known models")]
    public async Task AnUnknownModelNameComesBackAsACorrectableMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptConvene(test, UnknownModel, Gemma);
        test.Chat().Reply(FirstReply);

        var response = await Assistant(test).Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);

        var correction = Assert.Single(ToolResultsOfLastCall(test));
        Assert.Contains(UnknownModel, correction, StringComparison.Ordinal);
        Assert.All(
            TeamLineUp.KnownModels,
            known => Assert.Contains(known, correction, StringComparison.Ordinal));
        Assert.Equal(FirstReply, response.Text);
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(PairedTeam), 0, cancellationToken);
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "naming a single model comes back as a correctable message instead of running one model as a team")]
    public async Task ASingleModelComesBackAsACorrectableMessage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptConvene(test, Gemma);
        test.Chat().Reply(FirstReply);

        var response = await Assistant(test).Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);

        var correction = Assert.Single(ToolResultsOfLastCall(test));
        Assert.Contains("at least 2", correction, StringComparison.Ordinal);
        Assert.Equal(FirstReply, response.Text);
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(SoloTeam), 0, cancellationToken);
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "a failure that is not the team refusing the request never reaches the model as prose")]
    public async Task AFailureThatIsNotTheTeamRefusingNeverReachesTheModelAsProse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptConvene(test, Gemma, Llama);
        test.Chat().Reply(FirstReply);

        await using var storageOutage = test.Neuron<ITeam>(PairedTeam)
            .FailNextJournalCommit(StorageOutage);

        var response = await Assistant(test).Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);

        var reported = Assert.Single(ToolResultsOfLastCall(test));
        Assert.Equal(UndisclosedFailure, reported);
        Assert.Equal(FirstReply, response.Text);
        await AssertTurnsTakenAsync(test.Neuron<IGemma4>(PairedTeam), 0, cancellationToken);
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "the team capability is offered only when the owner's message names two models, so a bare greeting cannot convene one")]
    public async Task TheTeamCapabilityIsOfferedOnlyWhenTheMessageNamesTwoModels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(Greeting);
        test.Chat().Reply(FirstReply);

        var assistant = Assistant(test);
        await assistant.Respond([new ChatMessage(ChatRole.User, GreetingPrompt)]);
        var offeredToAGreeting = test.Chat().LastTools;

        await assistant.Respond([new ChatMessage(ChatRole.User, ComparePrompt)]);
        var offeredToAComparison = test.Chat().LastTools;

        Assert.Equal([EnrichAccountFromEmail], offeredToAGreeting);
        Assert.Contains(ConveneModelTeam, offeredToAComparison);
        Assert.Contains(EnrichAccountFromEmail, offeredToAComparison);
    }

    private static IAssistant Assistant(TestBrain test)
        => test.Client.GetGrainProxy<IAssistant>(AssistantName);

    private static void ScriptConvene(TestBrain test, params string[] models)
        => test.Chat().ReplyWithCapabilityCall(
            ConveneModelTeam,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["models"] = models,
                ["question"] = TeamQuestion,
            });

    private static void ScriptTeamTurn(TestBrain test, string first, string second)
    {
        test.Chat().Reply(first);
        test.Chat().Reply(second);
    }

    private static string[] ToolResultsOfLastCall(TestBrain test)
        => [.. test.Chat().LastMessages
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? string.Empty)];

    private static async Task AssertTurnsTakenAsync<TModel>(
        TestNeuron<TModel> model,
        int expected,
        CancellationToken cancellationToken)
        where TModel : class, ILLM
        => Assert.Equal(
            expected,
            (await model.Incoming.ReadAsync<CapabilityRequested>(
                afterSequence: 0, cancellationToken: cancellationToken))
            .Count(request => request.Synapse.Method == nameof(ILLM.RespondStreaming)));
}
