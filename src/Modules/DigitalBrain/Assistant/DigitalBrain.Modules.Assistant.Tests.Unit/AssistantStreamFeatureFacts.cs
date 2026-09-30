using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Specs;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Assistant.Tests.Unit;

public sealed class AssistantStreamFeatureFacts
{
    [Fact]
    public async Task MainApplicationStreamFeatureIsGreen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AssistantModule>().RequireModules([typeof(DigitalBrain.Apps.AppsModule), typeof(DigitalBrain.AI.AIModule), typeof(DigitalBrain.Compute.ComputeModule), typeof(DigitalBrain.Flutter.FlutterModule)]).WithModule<SpecsModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddSingleton<StepLibrary, AssistantStreamSteps>();
                silo.Services.AddSingleton<StreamScenarioRunner>();
                silo.Services.AddSingleton<IAgentTurnRunner>(services => services.GetRequiredService<StreamScenarioRunner>());
                silo.Services.AddSingleton<IAiCredentials>(new FixedAiCredentials().Ready("openai", "test-no-network"));
                silo.Services.Configure<AIOptions>(options =>
                {
                    options.Default.Provider = "OpenAI";
                    options.Default.Model = "gpt-4.1-mini";
                    options.Default.Capabilities = LlmCapabilities.Tools;
                });
            }).StartAsync(ct);
        using var resource = typeof(AssistantStreamFeatureFacts).Assembly.GetManifestResourceStream("assistant-stream.feature")!;
        using var reader = new StreamReader(resource);
        var feature = brain.Get<IFeature>("assistant-stream");
        var snapshot = await feature.Set(await reader.ReadToEndAsync(ct));
        Assert.True(snapshot.FullyBound, snapshot.Problem?.Message);
        var run = await feature.Run("stream-" + FeatureSnapshot.ScenarioPlaceholder + "/applications/assistant");
        Assert.True(run.Green, string.Join("\n", run.Scenarios.SelectMany(scenario => scenario.Steps
            .Where(step => step.Verdict != Verdict.Passed).Select(step => $"{scenario.Name}: {step.Verdict} {step.Message}"))));
    }
}

internal sealed partial class AssistantStreamSteps : StepLibrary
{
    public override string Name => "Assistant stream";
    public AssistantStreamSteps()
    {
        RegisterPresentationSteps();
        Step("the application streams and retains an isolated answer", "Run the real application neuron and read retained history.", async context =>
        {
            var app = App(context);
            var events = await Collect(app, new("one", "run", "hello", "owner"), context.CancellationToken);
            Assert.Equal(["RUN_STARTED", "TEXT_MESSAGE_START", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_END", "RUN_FINISHED", "RECEIPT"], events.Select(Type));
            Assert.Equal("Hello world", Text(events));
            Assert.Equal("Hello world", Assert.Single((await app.ReadConversation("one")).Turns).AssistantText);
            Assert.Empty((await app.ReadConversation("two")).Turns);
            Assert.Empty((await context.Grains.GetGrain<IAssistant>(AssistantSurface.Key(context.Subject.Split("/applications/")[0] + "-other")).ReadConversation("one")).Turns);
        });
        Step("the application replays once and rejects conflicting run input", "Replay does not invoke the model or duplicate history.", async context =>
        {
            var app = App(context);
            var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
            var request = new AssistantRun("one", "run", "replay", "owner");
            await Collect(app, request, context.CancellationToken);
            var before = runner.Count("replay");
            var replay = await Collect(app, request, context.CancellationToken);
            Assert.Equal("Hello world", Text(replay));
            Assert.Equal(before, runner.Count("replay"));
            Assert.DoesNotContain(replay, item => Type(item) == "RECEIPT");
            await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(app, request with { Message = "different" }, context.CancellationToken));
            var state = await app.ReadConversation("one");
            Assert.Null(state.ActiveRunId);
            Assert.Equal("replay", Assert.Single(state.Turns).UserText);
        });
        Step("an unavailable model leaves the conversation idle", "Model validation occurs before turn acquisition.", async context =>
        {
            var app = App(context);
            await Assert.ThrowsAsync<ArgumentException>(() => Collect(app, new("one", "run", "hello", "owner", "profile:missing"), context.CancellationToken));
            var state = await app.ReadConversation("one");
            Assert.Null(state.ActiveRunId);
            Assert.Empty(state.Turns);
            Assert.Contains(await Collect(app, new("one", "run", "hello", "owner"), context.CancellationToken), item => Type(item) == "RUN_FINISHED");
        });
        Step("a failed model turn permits a subsequent turn", "A failed model cannot leave the thread active.", async context =>
        {
            var app = App(context);
            Assert.Contains(await Collect(app, new("one", "failed", "fail", "owner"), context.CancellationToken), item => Type(item) == "RUN_ERROR");
            Assert.Null((await app.ReadConversation("one")).ActiveRunId);
            Assert.Contains(await Collect(app, new("one", "next", "hello", "owner"), context.CancellationToken), item => Type(item) == "RUN_FINISHED");
        });
        Step("a cancelled model turn permits a subsequent turn", "Cancellation propagates to the model and releases the active run.", async context =>
        {
            var app = App(context);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
            var reading = Collect(app, new("one", "cancelled", "wait", "owner"), cancel.Token);
            await runner.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(15), context.CancellationToken);
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
            await runner.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15), context.CancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((await app.ReadConversation("one")).ActiveRunId is not null && DateTime.UtcNow < deadline)
                { await Task.Delay(20, context.CancellationToken); }
            Assert.Null((await app.ReadConversation("one")).ActiveRunId);
            Assert.Empty((await app.ReadConversation("one")).Turns);
            Assert.Contains(await Collect(app, new("one", "next", "hello", "owner"), context.CancellationToken), item => Type(item) == "RUN_FINISHED");
        });
        Step("a conflicting active run cannot corrupt its owner", "Only the request which began the run may settle it.", async context =>
        {
            var app = App(context);
            var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
            var owner = Collect(app, new("one", "same", "hold", "owner"), context.CancellationToken);
            await runner.Holding.Task.WaitAsync(TimeSpan.FromSeconds(15), context.CancellationToken);
            try
            {
                var rejected = await Collect(app, new("one", "same", "intruder", "owner"), context.CancellationToken);
                Assert.Contains(rejected, item => Type(item) == "RUN_ERROR");
                var active = await app.ReadConversation("one");
                Assert.Equal("same", active.ActiveRunId);
                Assert.Empty(active.Turns);
            }
            finally { runner.Release.TrySetResult(); }
            Assert.Contains(await owner, item => Type(item) == "RUN_FINISHED");
            var state = await app.ReadConversation("one");
            Assert.Null(state.ActiveRunId);
            Assert.Equal("hold", Assert.Single(state.Turns).UserText);
            Assert.Equal(0, runner.Count("intruder"));
        });
        Step("the application forwards tools cards and a receipt", "The actual app stream preserves tool protocol and receipt activity.", async context =>
        {
            var events = await Collect(App(context), new("one", "tools", "tools", "owner"), context.CancellationToken);
            foreach (var type in new[] { "TOOL_CALL_START", "TOOL_CALL_ARGS", "TOOL_CALL_END", "TOOL_CALL_RESULT", "UI_CARD", "RECEIPT" })
                { Assert.Single(events, item => Type(item) == type); }
            Assert.Equal("table", events.Single(item => Type(item) == "UI_CARD").GetProperty("card").GetProperty("kind").GetString());
            var receipt = events.Single(item => Type(item) == "RECEIPT");
            Assert.Equal("Succeeded", receipt.GetProperty("outcome").GetString());
            Assert.NotEmpty(receipt.GetProperty("calls").EnumerateArray());
        });
    }
    private void Step(string pattern, string description, Func<StepContext, Task> action) => Step(pattern, description, (context, _) => action(context));
    private static IAssistant App(StepContext context) => context.Grains.GetGrain<IAssistant>(context.Subject);
    private static string? Type(JsonElement item) => item.GetProperty("type").GetString();
    private static string Text(IEnumerable<JsonElement> items) => string.Concat(items.Where(item => Type(item) == "TEXT_MESSAGE_CONTENT").Select(item => item.GetProperty("delta").GetString()));
    private static async Task<List<JsonElement>> Collect(IAssistant app, AssistantRun request, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        await foreach (var json in app.Run(request, ct))
        {
            using var document = JsonDocument.Parse(json);
            result.Add(document.RootElement.Clone());
        }
        return result;
    }
}

internal sealed class StreamScenarioRunner : IAgentTurnRunner
{
    private readonly ConcurrentDictionary<string, int> _calls = new();
    public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource UiHolding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Count(string message) => _calls.GetValueOrDefault(message);
    public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        _calls.AddOrUpdate(request.Message, 1, (_, count) => count + 1);
        if (request.Message == "ui-wait")
        {
            UiHolding.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        if (request.Message == "hold")
        {
            Holding.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
        if (request.Message == "wait")
        {
            Waiting.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { Cancelled.TrySetResult(); }
        }
        if (request.Message == "fail") { yield return new AgentTurnEvent.Failed("scripted failure"); yield break; }
        if (request.Message == "long-answer")
        {
            yield return new AgentTurnEvent.Text(new string('a', 32_000));
            yield return new AgentTurnEvent.Text(new string('b', 8_123));
            yield return new AgentTurnEvent.Finished();
            yield break;
        }
        if (request.Message == "window-handles")
        {
            yield return new AgentTurnEvent.ToolStarted("table-call", "show_supabase_query_table", "{}");
            yield return new AgentTurnEvent.ToolCompleted("table-call", "show_supabase_query_table", "{\"windowId\":\"table-result\",\"title\":\"Customers\",\"kind\":\"table\"}");
            yield return new AgentTurnEvent.ToolStarted("form-call", "show_form", "{}");
            yield return new AgentTurnEvent.ToolCompleted("form-call", "show_form", "{\"windowId\":\"form-result\",\"title\":\"Details\",\"formId\":\"form-neuron\"}");
        }
        if (request.Message == "tools")
        {
            yield return new AgentTurnEvent.ToolStarted("call", "show_supabase_query_table", "{}");
            yield return new AgentTurnEvent.ToolCompleted("call", "show_supabase_query_table", "{\"windowId\":\"window\",\"title\":\"Customers\",\"rowsRead\":2,\"_ui\":{\"kind\":\"table\"}}");
        }
        if (request.Message == "card-actions")
        {
            yield return new AgentTurnEvent.ToolStarted("reference", "reference", "{}");
            yield return new AgentTurnEvent.ToolCompleted("reference", "reference", """
                {"_ui":{"title":"Reference","actions":[
                  {"label":"Docs","url":"https://example.com"},
                  {"label":"Open table","id":"table-result","windowId":"table-result","remoteManaged":true,"kind":"table"}
                ]}}
                """);
        }
        yield return new AgentTurnEvent.Text("Hello ");
        yield return new AgentTurnEvent.Text("world");
        yield return new AgentTurnEvent.Finished();
    }
}
