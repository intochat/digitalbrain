using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Specs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Assistant.Tests.Unit;

internal sealed partial class AssistantStreamSteps
{
    private void RegisterPresentationSteps()
    {
        Step("tool card actions become explicit activation buttons", "Card links and workspace handles remain usable as generic buttons.", async context =>
        {
            var ui = await StartUi(context);
            await ui.Submit("card-actions");
            await ui.Until(thread => thread.Results.Count > 0 && thread.Messages.Any(message => message.Text == "Hello world") && thread.TurnId is null, context.CancellationToken);
            await App(context).Activate();
            var buttons = new List<ButtonState>();
            foreach (var node in (await ui.Tree()).Where(node => node.Kind == "button"))
            { buttons.Add(await context.Grains.GetGrain<IButton>(node.Name).Read()); }
            using var docs = System.Text.Json.JsonDocument.Parse(Assert.Single(buttons, button => button.Label == "Docs").Activation!);
            Assert.Equal("https://example.com", docs.RootElement.GetProperty("url").GetString());
            using var table = System.Text.Json.JsonDocument.Parse(Assert.Single(buttons, button => button.Label == "Open table").Activation!);
            Assert.Equal("table-result", table.RootElement.GetProperty("windowId").GetString());
            Assert.True(table.RootElement.GetProperty("remoteManaged").GetBoolean());
        });
        Step("a retired model profile does not prevent reopening", "A restored unavailable model remains visible as a disabled option.", async context =>
        {
            var ui = await StartUi(context);
            await App(context).RestoreLegacy("""
                {"selectedConversationId":"old","conversations":[{"id":"old","threadId":"retired-model-thread","title":"","draft":"Keep this","modelProfile":"profile:retired"}]}
                """);
            await context.Grains.GetGrain<IAssistant>(context.Subject).Activate();
            await ui.AssertExplicitControls();
            await ui.WaitForDraft("Keep this");
            var models = await ui.Select(AssistantSurface.ModelPart).Read();
            Assert.Equal("profile:retired", models.Selected);
            Assert.False(Assert.Single(models.Options, model => model.Id == "profile:retired").Enabled);
            Assert.All((await ui.Select(AssistantSurface.ThreadsPart).Read()).Options, option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
        });
        Step("a bound file input adds attachment content to the draft", "A generic file capture reaches the app without submitting a turn.", async context =>
        {
            var ui = await StartUi(context);
            await ui.Input("Review this");
            await context.Grains.GetGrain<IFileInput>(UiParts.NameOf(context.Subject, AssistantSurface.AttachPart))
                .Capture("notes.txt", "A useful note");
            var state = await ui.Until(thread => thread.Draft.Contains("A useful note", StringComparison.Ordinal), context.CancellationToken);
            Assert.Contains("Review this", state.Draft);
            Assert.Contains("notes.txt", state.Draft);
            Assert.Empty(state.Messages);
            Assert.Null(state.TurnId);
        });
        Step("explicit controls submit and restore thread drafts", "The app consumes explicit controls and publishes authoritative state.", async context =>
        {
            var ui = await StartUi(context);
            var first = (await ui.Read()).Id;
            await ui.Input("hello");
            await ui.Until(state => state.Draft == "hello", context.CancellationToken);
            await ui.Submit(null);
            var answer = await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, context.CancellationToken);
            Assert.Equal("", answer.Draft);
            Assert.Single(answer.Receipts);
            Assert.Equal("Hello world", Assert.Single((await App(context).ReadConversation(first)).Turns).AssistantText);
            await ui.Input("keep first draft");
            await ui.Until(state => state.Draft == "keep first draft", context.CancellationToken);
            await App(context).Activate();
            Assert.Equal("keep first draft", (await ui.Read()).Draft);
            await ui.Button(AssistantSurface.NewPart).Click();
            var second = await ui.Until(state => state.Id != first, context.CancellationToken);
            Assert.Empty(second.Messages);
            Assert.Equal("", second.Draft);
            await ui.Input("second draft");
            await ui.Until(state => state.Draft == "second draft", context.CancellationToken);
            await Assert.ThrowsAsync<ArgumentException>(() => ui.Select(AssistantSurface.ModelPart).Choose("profile:missing"));
            Assert.Null((await ui.Read()).ModelProfile);
            var model = (await ui.Select(AssistantSurface.ModelPart).Read()).Options.First(item => item.Enabled && item.Id.Length > 0).Id;
            await ui.Select(AssistantSurface.ModelPart).Choose(model);
            await ui.Until(state => state.ModelProfile == model && state.Error is null, context.CancellationToken);
            await ui.Select(AssistantSurface.ThreadsPart).Choose(first);
            var restored = await ui.Until(state => state.Id == first, context.CancellationToken);
            Assert.Equal("keep first draft", restored.Draft);
            Assert.Equal(2, restored.Messages.Count);
            await ui.Select(AssistantSurface.ThreadsPart).Choose(second.Id);
            var restoredSecond = await ui.Until(state => state.Id == second.Id, context.CancellationToken);
            Assert.Equal("second draft", restoredSecond.Draft);
            Assert.Equal(model, restoredSecond.ModelProfile);
        });
        Step("the stop button releases the running turn", "Cancellation travels through a bound button to the running app.", async context =>
        {
            var ui = await StartUi(context);
            await ui.Submit("ui-wait");
            var runner = context.Services.GetRequiredService<StreamScenarioRunner>();
            await runner.UiHolding.Task.WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
            var active = await ui.Until(state => state.TurnId is not null, context.CancellationToken);
            Assert.NotNull(active.TurnId);
            await ui.Button(AssistantSurface.StopPart).Click();
            await ui.Until(state => state.TurnId is null, context.CancellationToken);
            Assert.Null((await App(context).ReadConversation(active.Id)).ActiveRunId);
            await ui.Submit("hello");
            await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, context.CancellationToken);
        });
        Step("legacy conversation restoration preserves server history and new drafts", "Import old UI state once and prefer durable conversation turns.", async context =>
        {
            var ui = await StartUi(context);
            await Collect(App(context), new("saved-thread", "saved-run", "hello", "owner"), context.CancellationToken);
            const string legacy = """
                {"selectedConversationId":"local-id","conversations":[
                  {"id":"local-id","threadId":"saved-thread","title":"Saved conversation","draft":"old draft","modelProfile":"preset:IGpt56Luna","messages":[{"id":"stale","role":"assistant","text":"stale client reply"}]},
                  {"id":"local-only","threadId":"local-thread","title":"Local conversation","draft":"local draft","messages":[{"id":"local-message","role":"user","text":"local-only message"}]}
                ]}
                """;
            await App(context).RestoreLegacy(legacy);
            var restored = await ui.Read();
            Assert.Equal("saved-thread", restored.Id);
            Assert.Equal("old draft", restored.Draft);
            Assert.Equal("preset:IGpt56Luna", restored.ModelProfile);
            Assert.Contains((await App(context).Read()).Threads, thread => thread.Id == "saved-thread" && thread.Title == "Saved conversation");
            Assert.Contains(restored.Messages, message => message.Text == "Hello world");
            Assert.DoesNotContain(restored.Messages, message => message.Text == "stale client reply");
            await ui.Input("new authoritative draft");
            await ui.Until(state => state.Draft == "new authoritative draft", context.CancellationToken);
            await App(context).RestoreLegacy(legacy);
            await App(context).Activate();
            Assert.Equal("new authoritative draft", (await ui.Read()).Draft);
            Assert.Equal(2, (await App(context).Read()).Threads.Count);
            await ui.Select(AssistantSurface.ThreadsPart).Choose("local-thread");
            var local = await ui.Until(state => state.Id == "local-thread", context.CancellationToken);
            Assert.Equal("local draft", local.Draft);
            Assert.Equal("local-only message", Assert.Single(local.Messages).Text);
        });
        Step("tool window handles without UI metadata become result cards", "Production form and table results expose durable reopen handles.", async context =>
        {
            var ui = await StartUi(context);
            await ui.Submit("window-handles");
            var state = await ui.Until(state => state.Messages.Any(message => message.Text == "Hello world") && state.TurnId is null, context.CancellationToken);
            Assert.Equal(2, state.Results.Count);
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
        });
        Step("a long answer survives bounded chat projection and reopening", "The chat projection chunks long text without truncating retained history.", async context =>
        {
            var ui = await StartUi(context);
            var expected = new string('a', 32_000) + new string('b', 8_123);
            await ui.Submit("long-answer");
            var state = await ui.Until(state => state.Messages.Any(message => message.Role == ChatRole.Assistant && message.Text.Length > 0)
                && state.TurnId is null, context.CancellationToken);
            Assert.Null(state.Error);
            Assert.Contains(expected, await ui.VisibleText());
            Assert.Equal(expected, string.Concat(state.Messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text)));
            Assert.Equal(state.Messages.Count, state.Messages.Select(message => message.Id).Distinct().Count());
            Assert.Equal(expected, Assert.Single((await App(context).ReadConversation(state.Id)).Turns).AssistantText);
            await App(context).Activate();
            Assert.Equal(expected, string.Concat((await ui.Read()).Messages.Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text)));
        });
        Step("legacy artifact prompt suffixes remain only in durable history", "Both history restoration paths display only the owner's words.", async context =>
        {
            var ui = await StartUi(context);
            const string prompt = "Show customers\n\n[Conversation agent: private-artifact-context SECRET-CONTEXT]";
            await Collect(App(context), new("legacy-thread", "old-run", prompt, "owner"), context.CancellationToken);
            await Collect(App(context), new("other-thread", "other-run", prompt, "owner"), context.CancellationToken);
            const string legacy = """
                {"selectedConversationId":"old","conversations":[{"id":"old","threadId":"legacy-thread","title":"Show customers\n\n[Conversation agent: private-artifact-context SECRET-CONTEXT]"}]}
                """;
            await App(context).RestoreLegacy(legacy);
            var restored = await ui.Read();
            Assert.Equal("Show customers", restored.Messages.Single(message => message.Role == ChatRole.User).Text);
            Assert.Equal("Show customers", (await App(context).Read()).Threads.Single(thread => thread.Id == "legacy-thread").Title);
            Assert.DoesNotContain("SECRET-CONTEXT", System.Text.Json.JsonSerializer.Serialize(restored));
            Assert.Equal(prompt, Assert.Single((await App(context).ReadConversation("legacy-thread")).Turns).UserText);
            await App(context).Act("conversation", "other-thread");
            var selected = await ui.Until(state => state.Id == "other-thread", context.CancellationToken);
            Assert.Equal("Show customers", selected.Messages.Single(message => message.Role == ChatRole.User).Text);
            Assert.Equal("Show customers", (await App(context).Read()).Threads.Single(thread => thread.Id == "other-thread").Title);
            Assert.DoesNotContain("SECRET-CONTEXT", System.Text.Json.JsonSerializer.Serialize(selected));
            Assert.Equal(prompt, Assert.Single((await App(context).ReadConversation("other-thread")).Turns).UserText);
        });
        Step("two assistant windows have independent conversation state", "Each menu launch has its own surface, chat, and selected thread.", async context =>
        {
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
        });
    }
    private static async Task<AssistantUiProbe> StartUi(StepContext context)
    {
        await context.Grains.GetGrain<IAssistant>(context.Subject).Activate();
        var ui = new AssistantUiProbe(context.Grains, context.Subject);
        await ui.AssertExplicitControls();
        return ui;
    }
}
