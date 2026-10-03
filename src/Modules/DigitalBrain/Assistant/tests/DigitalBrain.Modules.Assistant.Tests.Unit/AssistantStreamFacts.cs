using DigitalBrain.Assistant;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Specs;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AssistantStreamFacts
{
    [Fact]
    public async Task KnownRefusalsReachThePersonWithoutModelRewriting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-refusal/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        foreach (var message in new[] { DigitalBrain.Sdk.Capacity.CapacityUnavailableException.RefusalMessage, "Cannot install this app. Missing modules: Postgres." })
        {
            var events = await Collect(app, new("one", Guid.NewGuid().ToString("N"), "refuse:" + message, "owner"), ct);
            Assert.Equal(message, Assert.Single(events, item => Type(item) == "RUN_ERROR").GetProperty("message").GetString());
        }
    }

    private static async Task<UnitBrain> StartBrain(CancellationToken ct) => await UnitTest.Create().WithExecution(new TestExecutionOptions
    {
        PrivateConfiguration = new Dictionary<string, string?> { ["DigitalBrain:Integrations:openai:ApiKey"] = "test-no-network" },
    }).WithModule<AssistantModule>()
        .RequireModules([typeof(DigitalBrain.Apps.AppsModule), typeof(DigitalBrain.AI.AIModule), typeof(DigitalBrain.Compute.ComputeModule), typeof(DigitalBrain.Flutter.FlutterModule)])
        .ConfigureSilo(silo =>
        {
            silo.Services.AddSingleton<StreamScenarioRunner>();
            silo.Services.AddSingleton<IAgentTurnRunner>(services => services.GetRequiredService<StreamScenarioRunner>());
            silo.Services.Configure<AIOptions>(options =>
            {
                options.Default.Provider = "OpenAI";
                options.Default.Model = "gpt-4.1-mini";
                options.Default.Capabilities = LlmCapabilities.Tools;
            });
        }).StartAsync(ct);

    [Fact]
    public async Task StreamedTextIsRetainedWithThreadAndWorkspaceIsolation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-streamedtext/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        var events = await Collect(app, new("one", "run", "hello", "owner"), ct);
        Assert.Equal(["RUN_STARTED", "TEXT_MESSAGE_START", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_CONTENT", "TEXT_MESSAGE_END", "RUN_FINISHED", "RECEIPT"], events.Select(Type));
        Assert.Equal("Hello world", Text(events));
        Assert.Equal("Hello world", Assert.Single((await app.ReadConversation("one", ct)).Turns).AssistantText);
        Assert.Empty((await app.ReadConversation("two", ct)).Turns);
        Assert.Empty((await context.Grains.GetGrain<IAssistant>(AssistantSurface.Key(context.Subject.Split("/applications/")[0] + "-other")).ReadConversation("one", ct)).Turns);
    }

    [Fact]
    public async Task ReplayIsIdempotentAndConflictingInputIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-replayisidem/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
        var request = new AssistantRun("one", "run", "replay", "owner");
        await Collect(app, request, ct);
        var before = runner.Count("replay");
        var replay = await Collect(app, request, ct);
        Assert.Equal("Hello world", Text(replay));
        Assert.Equal(before, runner.Count("replay"));
        Assert.DoesNotContain(replay, item => Type(item) == "RECEIPT");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(app, request with { Message = "different" }, ct));
        var state = await app.ReadConversation("one", ct);
        Assert.Null(state.ActiveRunId);
        Assert.Equal("replay", Assert.Single(state.Turns).UserText);
    }

    [Fact]
    public async Task UnavailableModelsDoNotAcquireAConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-unavailablem/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        await Assert.ThrowsAsync<ArgumentException>(() => Collect(app, new("one", "run", "hello", "owner", "profile:missing"), ct));
        var state = await app.ReadConversation("one", ct);
        Assert.Null(state.ActiveRunId);
        Assert.Empty(state.Turns);
        Assert.Contains(await Collect(app, new("one", "run", "hello", "owner"), ct), item => Type(item) == "RUN_FINISHED");
    }

    [Fact]
    public async Task FailedRunsReleaseTheConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-failedrunsre/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        var error = Assert.Single(await Collect(app, new("one", "failed", "fail", "owner"), ct), item => Type(item) == "RUN_ERROR");
        Assert.Equal("AGENT_FAILED", error.GetProperty("code").GetString());
        Assert.Equal("failed", error.GetProperty("runId").GetString());
        Assert.DoesNotContain("scripted failure", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain("data connection", error.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await app.ReadConversation("one", ct)).ActiveRunId);
        Assert.Contains(await Collect(app, new("one", "next", "hello", "owner"), ct), item => Type(item) == "RUN_FINISHED");
    }

    [Fact]
    public async Task CancellationReleasesTheConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-cancellation/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
        var reading = Collect(app, new("one", "cancelled", "wait", "owner"), cancel.Token);
        await runner.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await runner.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await app.ReadConversation("one", ct)).ActiveRunId is not null && DateTime.UtcNow < deadline)
        { await Task.Delay(20, ct); }
        Assert.Null((await app.ReadConversation("one", ct)).ActiveRunId);
        Assert.Empty((await app.ReadConversation("one", ct)).Turns);
        Assert.Contains(await Collect(app, new("one", "next", "hello", "owner"), ct), item => Type(item) == "RUN_FINISHED");
    }

    [Fact]
    public async Task ConcurrentSubmissionsCannotCompleteTheOwningRun()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-concurrentsu/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var app = App(context);
        var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
        var owner = Collect(app, new("one", "same", "hold", "owner"), ct);
        await runner.Holding.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        try
        {
            var rejected = await Collect(app, new("one", "same", "intruder", "owner"), ct);
            Assert.Contains(rejected, item => Type(item) == "RUN_ERROR");
            var active = await app.ReadConversation("one", ct);
            Assert.Equal("same", active.ActiveRunId);
            Assert.Empty(active.Turns);
        }
        finally { runner.Release.TrySetResult(); }
        Assert.Contains(await owner, item => Type(item) == "RUN_FINISHED");
        var state = await app.ReadConversation("one", ct);
        Assert.Null(state.ActiveRunId);
        Assert.Equal("hold", Assert.Single(state.Turns).UserText);
        Assert.Equal(0, runner.Count("intruder"));
    }

    [Fact]
    public async Task ToolEventsAndCardsTravelWithAnIntentReceipt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-tooleventsan/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var events = await Collect(App(context), new("one", "tools", "tools", "owner"), ct);
        foreach (var type in new[] { "TOOL_CALL_START", "TOOL_CALL_ARGS", "TOOL_CALL_END", "TOOL_CALL_RESULT", "UI_CARD", "RECEIPT" })
        { Assert.Single(events, item => Type(item) == type); }
        Assert.Equal("table", events.Single(item => Type(item) == "UI_CARD").GetProperty("card").GetProperty("kind").GetString());
        var receipt = events.Single(item => Type(item) == "RECEIPT");
        Assert.Equal("Succeeded", receipt.GetProperty("outcome").GetString());
        Assert.NotEmpty(receipt.GetProperty("calls").EnumerateArray());
    }

    [Fact]
    public async Task ToolCardsRetainTheirLinksAndWindowActions()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-toolcardsret/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        await ui.Submit("card-actions");
        await ui.Until(thread => thread.Results.Length > 0 && thread.Messages.Any(message => message.Text == "Hello world") && thread.TurnId is null, ct);
        await App(context).Activate();
        var buttons = new List<ButtonState>();
        foreach (var node in (await ui.Tree()).Where(node => node.Kind == "button"))
        { buttons.Add(await context.Grains.GetGrain<IButton>(node.Name).Read()); }
        using var docs = System.Text.Json.JsonDocument.Parse(Assert.Single(buttons, button => button.Label == "Docs").Activation!);
        Assert.Equal("https://example.com", docs.RootElement.GetProperty("url").GetString());
        using var table = System.Text.Json.JsonDocument.Parse(Assert.Single(buttons, button => button.Label == "Open table").Activation!);
        Assert.Equal("table-result", table.RootElement.GetProperty("windowId").GetString());
        Assert.True(table.RootElement.GetProperty("remoteManaged").GetBoolean());
    }

    [Fact]
    public async Task FileInputCapturesAttachmentContextWithoutSubmitting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-fileinputcap/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        await ui.Input("Review this");
        await context.Grains.GetGrain<IFileInput>(UiParts.NameOf(context.Subject, AssistantSurface.AttachPart))
            .Capture("notes.txt", "A useful note");
        var state = await ui.Until(thread => thread.Draft.Contains("A useful note", StringComparison.Ordinal), ct);
        Assert.Contains("Review this", state.Draft);
        Assert.Contains("notes.txt", state.Draft);
        Assert.Empty(state.Messages);
        Assert.Null(state.TurnId);
    }

    [Fact]
    public async Task ExplicitControlsOwnSubmissionAndRetainedDrafts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-explicitcont/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        var first = (await ui.Read()).Id;
        await ui.Input("hello");
        await ui.Until(state => state.Draft == "hello", ct);
        await ui.Submit(null);
        var answer = await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, ct);
        Assert.Equal("", answer.Draft);
        Assert.Single(answer.Receipts);
        Assert.Equal("Hello world", Assert.Single((await App(context).ReadConversation(first, ct)).Turns).AssistantText);
        await ui.Input("keep first draft");
        await ui.Until(state => state.Draft == "keep first draft", ct);
        await App(context).Activate();
        Assert.Equal("keep first draft", (await ui.Read()).Draft);
        await ui.Button(AssistantSurface.NewPart).Click();
        var second = await ui.Until(state => state.Id != first, ct);
        Assert.Empty(second.Messages);
        Assert.Equal("", second.Draft);
        await ui.Input("second draft");
        await ui.Until(state => state.Draft == "second draft", ct);
        await Assert.ThrowsAsync<ArgumentException>(() => ui.Select(AssistantSurface.ModelPart).Choose("profile:missing"));
        Assert.Null((await ui.Read()).ModelProfile);
        var model = (await ui.Select(AssistantSurface.ModelPart).Read()).Options.First(item => item.Enabled && item.Id.Length > 0).Id;
        await ui.Select(AssistantSurface.ModelPart).Choose(model);
        await ui.Until(state => state.ModelProfile == model && state.Error is null, ct);
        await ui.Select(AssistantSurface.ThreadsPart).Choose(first);
        var restored = await ui.Until(state => state.Id == first, ct);
        Assert.Equal("keep first draft", restored.Draft);
        Assert.Equal(2, restored.Messages.Length);
        await ui.Select(AssistantSurface.ThreadsPart).Choose(second.Id);
        var restoredSecond = await ui.Until(state => state.Id == second.Id, ct);
        Assert.Equal("second draft", restoredSecond.Draft);
        Assert.Equal(model, restoredSecond.ModelProfile);
    }

    [Fact]
    public async Task ExplicitControlsCancelBusyTurns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-explicitcont/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        await ui.Submit("ui-wait");
        var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
        await runner.UiHolding.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        var active = await ui.Until(state => state.TurnId is not null, ct);
        Assert.NotNull(active.TurnId);
        await ui.Button(AssistantSurface.StopPart).Click();
        await ui.Until(state => state.TurnId is null, ct);
        Assert.Null((await App(context).ReadConversation(active.Id, ct)).ActiveRunId);
        await ui.Submit("hello");
        await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, ct);
    }

    [Fact]
    public async Task ProductionToolHandlesBecomeReopenableResultCards()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-productionto/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        await ui.Submit("window-handles");
        var state = await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, ct);
        Assert.Equal(2, state.Results.Length);
        foreach (var expected in new[] { (Id: "table-result", Kind: "table", Title: "Customers"), (Id: "form-result", Kind: "surface", Title: "Details") })
        {
            using var document = System.Text.Json.JsonDocument.Parse(state.Results.Single(json => json.Contains(expected.Id, StringComparison.Ordinal)));

            var card = document.RootElement.TryGetProperty("card", out var wrapped) ? wrapped : document.RootElement;
            Assert.Equal(expected.Id, card.GetProperty("id").GetString());
            Assert.Equal(expected.Id, card.GetProperty("windowId").GetString());
            Assert.Equal(expected.Kind, card.GetProperty("kind").GetString());
            Assert.Equal(expected.Title, card.GetProperty("title").GetString());
            Assert.True(card.GetProperty("remoteManaged").GetBoolean());
        }
        await App(context).Activate();
        Assert.Equal(state.Results, (await ui.Read()).Results);
        var openButtons = (await ui.Tree()).Where(node => node.Kind == "button" && node.Name.EndsWith("/open", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, openButtons.Length);
        foreach (var node in openButtons)
        {
            var button = await context.Grains.GetGrain<IButton>(node.Name).Read();
            Assert.Equal("Open", button.Label);
            using var activation = System.Text.Json.JsonDocument.Parse(button.Activation!);
            Assert.True(activation.RootElement.GetProperty("remoteManaged").GetBoolean());
            Assert.Contains(activation.RootElement.GetProperty("windowId").GetString(), new[] { "table-result", "form-result" });
        }
    }

    [Fact]
    public async Task LongModelAnswersRemainCompleteAndReopenable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-longmodelans/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        var expected = new string('a', 32_000) + new string('b', 8_123);
        await ui.Submit("long-answer");
        var state = await ui.Until(state => state.Messages.Any(message => message.Role == ChatRole.Assistant && message.Text.Length > 0)
            && state.TurnId is null, ct);
        Assert.Null(state.Error);
        Assert.Contains(expected, await ui.VisibleText());
        Assert.Equal(expected, string.Concat(state.Messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text)));
        Assert.Equal(state.Messages.Length, state.Messages.Select(message => message.Id).Distinct().Count());
        Assert.Equal(expected, Assert.Single((await App(context).ReadConversation(state.Id, ct)).Turns).AssistantText);
        await App(context).Activate();
        Assert.Equal(expected, string.Concat((await ui.Read()).Messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text)));
    }

    [Fact]
    public async Task HistoricalArtifactContextStaysOutOfVisibleConversations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-historicalar/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var ui = await StartUi(context);
        const string prompt = "Show customers\n\n[Conversation agent: private-artifact-context SECRET-CONTEXT]";
        await Collect(App(context), new("legacy-thread", "old-run", prompt, "owner"), ct);
        await Collect(App(context), new("other-thread", "other-run", prompt, "owner"), ct);
        await App(context).Act("conversation", "legacy-thread");
        var restored = await ui.Until(state => state.Id == "legacy-thread", ct);
        Assert.Equal("Show customers", restored.Messages.Single(message => message.Role == ChatRole.User).Text);
        Assert.Equal("Show customers", (await App(context).Read()).Threads.Single(thread => thread.Id == "legacy-thread").Title);
        Assert.DoesNotContain("SECRET-CONTEXT", System.Text.Json.JsonSerializer.Serialize(restored));
        Assert.Equal(prompt, Assert.Single((await App(context).ReadConversation("legacy-thread", ct)).Turns).UserText);
        await App(context).Act("conversation", "other-thread");
        var selected = await ui.Until(state => state.Id == "other-thread", ct);
        Assert.Equal("Show customers", selected.Messages.Single(message => message.Role == ChatRole.User).Text);
        Assert.Equal("Show customers", (await App(context).Read()).Threads.Single(thread => thread.Id == "other-thread").Title);
        Assert.DoesNotContain("SECRET-CONTEXT", System.Text.Json.JsonSerializer.Serialize(selected));
        Assert.Equal(prompt, Assert.Single((await App(context).ReadConversation("other-thread", ct)).Turns).UserText);
    }

    [Fact]
    public async Task AppLaunchesCreateIndependentWorkspaceWindows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartBrain(ct);
        var context = new StepContext("stream-applaunchesc/applications/assistant", brain.Grains, brain.SiloServices, ct);
        var first = await App(context).OpenWindow();
        var second = await App(context).OpenWindow();
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Surface.Name, second.Surface.Name);
        var left = new AssistantUiProbe(context.Grains, first.Surface.Name[..^"/surface".Length]);
        var right = new AssistantUiProbe(context.Grains, second.Surface.Name[..^"/surface".Length]);
        await left.AssertExplicitControls();
        await right.AssertExplicitControls();
        await left.Input("left only");
        Assert.Equal("", (await right.Read()).Draft);
        Assert.NotEqual((await left.Read()).Id, (await right.Read()).Id);
        var windows = (await context.Grains.GetGrain<IWorkspace>(context.Subject.Split("/applications/")[0]).Read()).Windows;
        Assert.Contains(windows, window => window.Id == first.Id && window.IsOpen);
        Assert.Contains(windows, window => window.Id == second.Id && window.IsOpen);
    }

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

    private static async Task<AssistantUiProbe> StartUi(StepContext context)
    {
        await context.Grains.GetGrain<IAssistant>(context.Subject).Activate();
        var ui = new AssistantUiProbe(context.Grains, context.Subject);
        await ui.AssertExplicitControls();
        return ui;
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
        if (request.Message.StartsWith("refuse:", StringComparison.Ordinal)) { yield return new AgentTurnEvent.Failed(request.Message[7..]); yield break; }
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
