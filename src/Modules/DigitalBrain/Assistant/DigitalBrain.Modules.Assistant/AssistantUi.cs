using System.Globalization;
using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.Text;
using Orleans;

namespace DigitalBrain.Assistant;

internal sealed partial class AssistantNeuron
{
    private readonly Dictionary<string, string> _published = new(StringComparer.Ordinal);

    private async Task Present()
    {
        await _presentation.WaitAsync();
        try
        {
            var models = await Models();
            var thread = Thread(Snapshot.SelectedThread);
            var busy = _running.ContainsKey(thread.Id);
            await Publish(AssistantSurface.DraftPart, thread.Draft, () => Draft.SetValue(thread.Draft));
            await Choices(AssistantSurface.ThreadsPart, "Conversation",
                Snapshot.Threads.Select(item => new SelectOption(item.Id, string.IsNullOrWhiteSpace(item.Title) ? "Conversation" : item.Title)).ToArray(), thread.Id);
            var options = new[] { models.Automatic }.Concat(models.Models)
                .Select(model => new SelectOption(model.Id ?? "", model.Label, model.Available)).ToList();
            if (thread.ModelProfile is { } profile && options.All(option => option.Id != profile))
            { options.Add(new(profile, profile + " (unavailable)", false)); }
            await Choices(AssistantSurface.ModelPart, "Model", options.ToArray(), thread.ModelProfile ?? "");
            await Publish(AssistantSurface.StatusPart, thread.Error ?? thread.Status ?? "", () =>
                GrainFactory.GetGrain<IText>(Name(AssistantSurface.StatusPart)).Set(thread.Error ?? thread.Status ?? ""));
            await Publish(AssistantSurface.SendPart, !busy, () =>
                GrainFactory.GetGrain<IButton>(Name(AssistantSurface.SendPart)).Set("Send", "submit", !busy));
            await Publish(AssistantSurface.StopPart, busy, () =>
                GrainFactory.GetGrain<IButton>(Name(AssistantSurface.StopPart)).Set("Stop", "cancel", busy));

            var messages = new List<UiChildRef>();
            foreach (var message in thread.Messages)
            {
                var part = "message/" + message.Id;
                await Card(part, message.Role == ChatRole.User ? "You" : "Assistant", message.Text);
                messages.Add(new(UIVocabulary.CardType, Name(part)));
            }
            await List(AssistantSurface.MessagesPart, messages);

            var results = new List<UiChildRef>();
            for (var index = 0; index < thread.Results.Count; index++)
            {
                using var document = JsonDocument.Parse(thread.Results[index]);
                var result = document.RootElement;
                if (result.TryGetProperty("card", out var card)) { result = card; }
                var part = $"result/{thread.Id}/{index}";
                var title = JsonText(result, "title") ?? "Result";
                var body = JsonText(result, "body") ?? JsonText(result, "text") ?? JsonText(result, "description") ?? "";
                var children = new List<UiChildRef>();
                if (result.TryGetProperty("id", out _) || result.TryGetProperty("windowId", out _) || result.TryGetProperty("url", out _))
                {
                    var button = part + "/open";
                    var activation = result.GetRawText();
                    await Publish(button, activation, () => GrainFactory.GetGrain<IButton>(Name(button)).SetActivation("Open", activation));
                    children.Add(new(UIVocabulary.ButtonType, Name(button)));
                }
                if (result.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
                {
                    var actionIndex = 0;
                    foreach (var action in actions.EnumerateArray().Take(32))
                    {
                        if (action.ValueKind != JsonValueKind.Object) { continue; }
                        var button = part + "/action/" + actionIndex++;
                        var label = JsonText(action, "label") ?? JsonText(action, "title") ?? "Open";
                        var declaration = action.GetRawText();
                        if (JsonText(action, "action") is not null)
                        {
                            await Publish(button, declaration, async () =>
                            {
                                await GrainFactory.GetGrain<IButton>(Name(button)).Set(label, "command:" + declaration);
                                await GrainFactory.GetGrain<IUiBinding>(Name(button)).Bind(this.AsReference<IUiEventHandler>());
                            });
                        }
                        else
                        { await Publish(button, declaration, () => GrainFactory.GetGrain<IButton>(Name(button)).SetActivation(label, declaration)); }
                        children.Add(new(UIVocabulary.ButtonType, Name(button)));
                    }
                }
                await Card(part, title, body, children);
                results.Add(new(UIVocabulary.CardType, Name(part)));
            }
            await List(AssistantSurface.ResultsPart, results);

            var receipts = new List<UiChildRef>();
            for (var index = 0; index < thread.Receipts.Count; index++)
            {
                var part = $"receipt/{thread.Id}/{index}";
                await Card(part, "Receipt", ReceiptBody(thread.Receipts[index]));
                receipts.Add(new(UIVocabulary.CardType, Name(part)));
            }
            await List(AssistantSurface.ReceiptsPart, receipts);
        }
        finally { _presentation.Release(); }
    }

    private string Name(string part) => UiParts.NameOf(Key, part);

    private Task Choices(string part, string label, SelectOption[] options, string selected) =>
        Publish(part, new { options, selected }, () => GrainFactory.GetGrain<ISelect>(Name(part)).Set(label, options, selected));

    private Task Card(string part, string title, string body, IReadOnlyList<UiChildRef>? children = null) =>
        Publish(part, new { title, body, children }, () => GrainFactory.GetGrain<ICard>(Name(part)).Set(title, body, children));

    private async Task List(string part, IReadOnlyList<UiChildRef> children)
    {
        // Keep every retained message while respecting the composition's bounded fan-out.
        if (children.Count > 128)
        {
            var groups = new List<UiChildRef>();
            foreach (var (chunk, index) in children.Chunk(100).Select((chunk, index) => (chunk, index)))
            {
                var group = part + "/page/" + index;
                await List(group, chunk);
                groups.Add(new(UIVocabulary.LayoutType, Name(group)));
            }
            children = groups;
        }
        var definition = new LayoutDefinition("list", children.ToArray(), Gap: 8);
        await Publish(part, definition, async () =>
        {
            var layout = GrainFactory.GetGrain<ILayout>(Name(part));
            await layout.Set(definition, (await layout.Read()).Revision);
        });
    }

    private async Task Publish<T>(string part, T value, Func<Task> update)
    {
        var serialized = JsonSerializer.Serialize(value);
        if (_published.TryGetValue(part, out var previous) && previous == serialized) { return; }
        await update();
        _published[part] = serialized;
    }

    private static string ReceiptBody(string json)
    {
        using var document = JsonDocument.Parse(json);
        var receipt = document.RootElement;
        var lines = new List<string>();
        if (JsonText(receipt, "summary") is { } summary) { lines.Add(summary); }
        if (receipt.TryGetProperty("calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            var names = calls.EnumerateArray().Select(call => JsonText(call, "appId")).Where(name => name is not null);
            if (names.Any()) { lines.Add("What ran: " + string.Join(", ", names)); }
        }
        if (receipt.TryGetProperty("touched", out var touched) && touched.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in touched.EnumerateArray())
            { lines.Add($"{JsonText(item, "source")} · {(item.TryGetProperty("rowsRead", out var rows) ? rows.ToString() : "0")} rows read"); }
        }
        var compute = receipt.TryGetProperty("compute", out var amount) && amount.TryGetDecimal(out var price) ? price : 0;
        lines.Add(compute.ToString("0.####", CultureInfo.InvariantCulture) + " Compute" +
            (receipt.TryGetProperty("shadow", out var shadow) && shadow.ValueKind == JsonValueKind.False ? "" : " · preview price, not charged"));
        return string.Join("\n\n", lines);
    }
}
