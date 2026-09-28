using System.Text.Json;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Apps.Assistant;

[GenerateSerializer, Alias("assistant.thread")]
public sealed record AssistantThread
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public string Title { get; init; } = "New conversation";
    [Id(2)] public string Draft { get; init; } = "";
    [Id(3)] public string? ModelProfile { get; init; }
    [Id(4)] public IReadOnlyList<ChatEntry> Messages { get; init; } = [];
    [Id(5)] public IReadOnlyList<string> Receipts { get; init; } = [];
    [Id(6)] public IReadOnlyList<string> Results { get; init; } = [];
    [Id(7)] public string? Error { get; init; }
    [Id(8)] public string? Status { get; init; }
    [Id(9)] public string? TurnId { get; init; }
}

[GenerateSerializer, Alias("assistant.window")]
public sealed record AssistantWindow(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] UiChildRef Surface);

internal sealed partial class AssistantNeuron
{
    private readonly Dictionary<string, CancellationTokenSource> _running = [];
    private readonly SemaphoreSlim _changes = new(1);
    private readonly SemaphoreSlim _presentation = new(1);

    public async Task RestoreLegacy(string projectJson)
    {
        if (Snapshot.LegacyRestored) { return; }
        using var document = JsonDocument.Parse(projectJson);
        var project = document.RootElement;
        if (!project.TryGetProperty("conversations", out var conversations) || conversations.ValueKind != JsonValueKind.Array) { return; }
        var restored = new List<AssistantThread>();
        string? selected = null;
        var previousSelection = JsonText(project, "selectedConversationId");
        foreach (var item in conversations.EnumerateArray().Take(200))
        {
            var id = JsonText(item, "threadId") ?? JsonText(item, "id");
            if (id is null) { continue; }
            var history = await ReadConversation(id);
            var messages = history.Turns.SelectMany(turn => new[]
            {
                new ChatEntry(turn.RunId + "-user", ChatRole.User, DisplayUserText(turn.UserText)),
                new ChatEntry(turn.RunId + "-reply", ChatRole.Assistant, turn.AssistantText),
            }).TakeLast(1000).ToArray();
            if (messages.Length == 0 && item.TryGetProperty("messages", out var saved) && saved.ValueKind == JsonValueKind.Array)
            {
                messages = saved.EnumerateArray().Where(entry => JsonText(entry, "text") is not null
                    && (JsonText(entry, "role") is null or "user" or "assistant")).TakeLast(1000)
                    .Select(entry => new ChatEntry(JsonText(entry, "id") ?? Guid.NewGuid().ToString("N"),
                        (JsonText(entry, "role") ?? JsonText(entry, "authorId")) is "user" or "you" ? ChatRole.User : ChatRole.Assistant,
                        (JsonText(entry, "role") ?? JsonText(entry, "authorId")) is "user" or "you" ? DisplayUserText(JsonText(entry, "text")!) : JsonText(entry, "text")!)).ToArray();
            }
            restored.Add(new()
            {
                Id = id, Title = DisplayUserText(JsonText(item, "title") ?? "Conversation"), Draft = Bounded(JsonText(item, "draft") ?? ""),
                ModelProfile = JsonText(item, "modelProfile"), Messages = messages,
            });
            if (JsonText(item, "id") == previousSelection) { selected = id; }
        }
        await Change(state =>
        {
            if (state.LegacyRestored) { return state; }
            var existing = state.Threads.Where(thread => thread.Messages.Count > 0 || thread.Draft.Length > 0 || _running.ContainsKey(thread.Id)).ToList();
            var added = restored.Where(thread => existing.All(current => current.Id != thread.Id)).DistinctBy(thread => thread.Id).ToList();
            var merged = existing.Concat(added).ToArray();
            return state with
            {
                LegacyRestored = true, Threads = merged.Length == 0 ? state.Threads : merged,
                SelectedThread = existing.Any(thread => thread.Id == state.SelectedThread) ? state.SelectedThread
                    : merged.FirstOrDefault(thread => thread.Id == selected)?.Id ?? merged.FirstOrDefault()?.Id ?? state.SelectedThread,
            };
        });
        await Present();
    }

    private static string? JsonText(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public async Task SetDraft(string draft)
    {
        if (Snapshot.Threads.Count == 0) { await InitializePresentation(); }
        await ChangeThread(Snapshot.SelectedThread, thread => thread with { Draft = Bounded(draft) });
        await Present();
    }

    public async Task<AssistantWindow> OpenWindow(string? draft = null, string? title = null)
    {
        if (draft is not null) { Bounded(draft); }
        title = string.IsNullOrWhiteSpace(title) ? "Assistant" : title.Trim();
        if (title.Length > 200) { throw new ArgumentException("A window title has at most 200 characters."); }
        var id = "assistant-" + Guid.NewGuid().ToString("N");
        var key = AssistantApp.Key(Workspace) + "/" + id;
        await services.GetRequiredService<ApplicationCatalog>().Start(AppDefinition.NameOf<AssistantApp>(), key);
        if (draft is not null) { await GrainFactory.GetGrain<IAssistant>(key).SetDraft(draft); }
        var surface = new UiChildRef(UIVocabulary.SurfaceType, UiComposer.NameOf(key, "surface"));
        await GrainFactory.GetGrain<IWorkspace>(Workspace).EnsureOpenAsync(id, title, WindowReference.For(surface), CancellationToken.None);
        return new(id, title, surface);
    }

    private async Task InitializePresentation()
    {
        if (Snapshot.Threads.Count == 0)
        {
            var id = "conversation-" + Guid.NewGuid().ToString("N");
            var draft = (await Draft.Read()).Value;
            await Change(state => state with
            {
                SelectedThread = id,
                Owner = CallerContextStamper.TryGet(out var caller) ? caller.AccountId : Workspace,
                Threads = [new() { Id = id, Draft = draft }],
            });
        }
        await Present();
    }

    // UI actions are commands on the application neuron. Flutter only sends these
    // commands and renders the declared primitives; it never executes an Assistant turn.
    public async Task Act(string action, string? value)
    {
        try
        {
            if (Snapshot.Threads.Count == 0) { await InitializePresentation(); }
            var id = Snapshot.SelectedThread;
            switch (action)
            {
                case "draft":
                    await ChangeThread(id, thread => thread with { Draft = Bounded(value ?? "") });
                    break;
                case "submit":
                    await Send(value ?? Thread(id).Draft);
                    return;
                case "cancel":
                    if (_running.TryGetValue(id, out var pending)) { await pending.CancelAsync(); }
                    break;
                case "model":
                    var model = string.IsNullOrEmpty(value) ? null : value;
                    new AssistantTurnExecution(services, GrainFactory).Models.Select(model);
                    await ChangeThread(id, thread => thread with { ModelProfile = model, Error = null });
                    break;
                case "new-conversation":
                    var created = new AssistantThread { Id = "conversation-" + Guid.NewGuid().ToString("N") };
                    await Change(state => state with { Threads = [.. state.Threads, created], SelectedThread = created.Id });
                    break;
                case "conversation":
                    await Conversation(value ?? ""); // Reject malformed thread ids before reading history.
                    if (!Snapshot.Threads.Any(thread => thread.Id == value))
                    {
                        var history = await ReadConversation(value!);
                        var first = history.Turns.FirstOrDefault() is { } firstTurn ? DisplayUserText(firstTurn.UserText) : null;
                        var restored = new AssistantThread
                        {
                            Id = value!, Title = first is null ? "Conversation" : first[..Math.Min(60, first.Length)],
                            Messages = history.Turns.SelectMany(turn => new[]
                            {
                                new ChatEntry(turn.RunId + "-user", ChatRole.User, DisplayUserText(turn.UserText)),
                                new ChatEntry(turn.RunId + "-reply", ChatRole.Assistant, turn.AssistantText),
                            }).ToArray(),
                        };
                        await Change(state => state with { Threads = [.. state.Threads, restored] });
                    }
                    await Change(state => state with { SelectedThread = value! });
                    break;
                case "attach":
                    using (var attachment = JsonDocument.Parse(value ?? "{}"))
                    {
                        var name = attachment.RootElement.GetProperty("name").GetString();
                        var content = attachment.RootElement.GetProperty("content").GetString();
                        await ChangeThread(id, thread => thread with { Draft = Bounded(thread.Draft + $"\n\nAttached file: {name}\n{content}") });
                    }
                    break;
                case "voice":
                    var transcript = await Transcribe(value);
                    await ChangeThread(id, thread => transcript.Status == 200
                        ? thread with { Draft = Bounded(transcript.Text ?? ""), Error = null }
                        : thread with { Error = transcript.Error });
                    break;
                default: throw new ArgumentException("Unknown conversation action.");
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Conversation action {Action} failed", action);
            if (Snapshot.Threads.Count > 0)
            { await ChangeThread(Snapshot.SelectedThread, thread => thread with { Error = error is ArgumentException ? error.Message : "The action could not be completed. Please try again." }); }
        }
        await Present();
    }

    private async Task Send(string message)
    {
        if (Snapshot.Threads.Count == 0) { await InitializePresentation(); }
        message = Bounded(message.Trim());
        if (message.Length == 0) { return; }
        var id = Snapshot.SelectedThread;
        if (_running.ContainsKey(id)) { return; }
        using var lifetime = new CancellationTokenSource();
        _running.Add(id, lifetime);
        var run = Guid.NewGuid().ToString("N");
        var profile = Thread(id).ModelProfile;
        try
        {
            await ChangeThread(id, thread => thread with
            {
                Draft = "", Error = null, Status = "Thinking…", TurnId = run,
                Title = thread.Messages.Count == 0 ? message[..Math.Min(60, message.Length)] : thread.Title,
                Messages = [.. thread.Messages.TakeLast(998), new(run + "-user", ChatRole.User, message), new(run + "-reply", ChatRole.Assistant, "")],
            });
            await Present();
            await foreach (var item in Run(new(id, run, message, Snapshot.Owner, profile), lifetime.Token))
            {
                using var document = JsonDocument.Parse(item);
                var data = document.RootElement;
                switch (data.GetProperty("type").GetString())
                {
                    case "TEXT_MESSAGE_CONTENT":
                        var delta = data.GetProperty("delta").GetString() ?? "";
                        await ChangeThread(id, thread => thread with
                        {
                            Messages = thread.Messages.Select(entry => entry.Id == run + "-reply" ? entry with { Text = entry.Text + delta } : entry).ToArray(),
                        });
                        break;
                    case "TOOL_CALL_START":
                        await ChangeThread(id, thread => thread with { Status = "Using " + data.GetProperty("toolCallName").GetString() });
                        break;
                    case "UI_CARD":
                        await ChangeThread(id, thread => thread with { Results = [.. thread.Results.TakeLast(99), item] });
                        break;
                    case "TOOL_CALL_RESULT":
                        if (ResultCard(data) is { } card)
                        { await ChangeThread(id, thread => thread with { Results = [.. thread.Results.TakeLast(99), card] }); }
                        break;
                    case "RECEIPT":
                        await ChangeThread(id, thread => thread with { Receipts = [.. thread.Receipts.TakeLast(99), item] });
                        break;
                    case "RUN_ERROR":
                        await ChangeThread(id, thread => thread with { Error = data.GetProperty("message").GetString() });
                        break;
                }
                await Present();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        { await ChangeThread(id, thread => thread with { Status = "Stopped" }); }
        catch (Exception error)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Conversation presentation run failed");
            await ChangeThread(id, thread => thread with { Error = error is ArgumentException ? error.Message : "The request could not be completed. Please try again." });
        }
        finally
        {
            _running.Remove(id);
            await ChangeThread(id, thread => thread with { Status = null, TurnId = null });
            await Present();
        }
    }

    private AssistantThread Thread(string id) => Snapshot.Threads.Single(thread => thread.Id == id);

    private Task ChangeThread(string id, Func<AssistantThread, AssistantThread> change) => Change(state => state with
    { Threads = state.Threads.Select(thread => thread.Id == id ? change(thread) : thread).ToArray() });

    private async Task Change(Func<AssistantState, AssistantState> change)
    {
        await _changes.WaitAsync();
        try
        {
            var next = change(Snapshot);
            // Persist concrete arrays: collection expressions targeting IReadOnlyList can
            // create compiler-private types which the storage serializer cannot restore.
            next = next with
            {
                Threads = next.Threads.Select(thread => thread with
                {
                    Messages = thread.Messages.ToArray(), Receipts = thread.Receipts.ToArray(), Results = thread.Results.ToArray(),
                }).ToArray(),
            };
            await Save(next, new AssistantConfigured(Key));
        }
        finally { _changes.Release(); }
    }

    private static string Bounded(string value) => value.Length <= 32000 ? value : throw new ArgumentException("A message or draft has at most 32000 characters.");

    private static string DisplayUserText(string value) => value.Split("\n\n[Conversation agent:", 2, StringSplitOptions.None)[0];

    private static string? ResultCard(JsonElement item)
    {
        try
        {
            if (!item.TryGetProperty("content", out var content)) { return null; }
            using var parsed = JsonDocument.Parse(content.ValueKind == JsonValueKind.String ? content.GetString()! : content.GetRawText());
            var result = parsed.RootElement;
            if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("_ui", out _)) { return null; }
            string? Text(string name) => result.EnumerateObject().FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .Value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
            if (result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True) { return null; }
            if (Text("windowId") is { } window)
            {
                return JsonSerializer.Serialize(new { id = window, windowId = window, remoteManaged = true,
                    kind = Text("formId") is null ? Text("kind") ?? "table" : "surface", title = Text("title") ?? "Result" });
            }
            if (Text("id") is not null && Text("kind") is "table" or "chart" or "graph" or "diagram" or "brain" or "image" or "document" or "surface")
            { return result.GetRawText(); }
        }
        catch (JsonException) { /* Plain tool output has no window handle. */ }
        return null;
    }
}
