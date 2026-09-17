using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Behavior;
using Microsoft.Extensions.AI;
using Orleans;
using Orleans.Runtime;

namespace IntoChat;

[Alias("intochat.conversation")]
internal interface IConversation : IGrainWithStringKey
{
    Task<JsonElement> PrepareInput(string requestHash, JsonElement suppliedMessages);
    Task CompleteRun(BehaviorRunSnapshot run);
}

// Default, non-reentrant scheduling keeps the history check and write in one grain request.
[GrainType("intochat-conversation")]
internal sealed class ConversationGrain(
    [PersistentState("history", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ConversationState> history)
    : Grain, IConversation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool _reloadRequired;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (history.RecordExists)
        {
            return;
        }

        // Import the previous signal-based history once; all subsequent writes belong to this grain.
        var legacyId = NeuronId.Plain("conversation-" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(this.GetPrimaryKeyString())))[..32]);
        var legacy = await GrainFactory.GetGrain<INeuron>(legacyId.ToGrainId()).ReadState();
        if (legacy.FirstOrDefault(item => item.Signal.Type == "ConversationHistory")?.Signal.Body is { } body)
        {
            using var document = JsonDocument.Parse(body);
            var value = document.RootElement;
            var messages = value.ValueKind == JsonValueKind.Array ? value : value.GetProperty("messages");
            var lastRunId = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("lastRunId", out var runId)
                ? runId.GetString() : null;
            var completedAt = value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("completedAt", out var timestamp) && timestamp.ValueKind != JsonValueKind.Null
                    ? timestamp.GetDateTimeOffset() : (DateTimeOffset?)null;
            await SaveAsync(new(lastRunId, completedAt, messages.GetRawText()));
        }
    }

    public async Task<JsonElement> PrepareInput(string requestHash, JsonElement suppliedMessages)
    {
        await ReloadIfRequiredAsync();
        using var document = JsonDocument.Parse(history.State.MessagesJson);
        var messages = BehaviorConversationHistory.Read(document.RootElement);
        messages.AddRange(BehaviorConversationHistory.Read(suppliedMessages));
        BehaviorConversationHistory.Trim(messages, BehaviorConversationHistory.InputBudget);
        return JsonSerializer.SerializeToElement(new { threadId = this.GetPrimaryKeyString(), requestHash, messages }, Json);
    }

    public async Task CompleteRun(BehaviorRunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != "Completed" || run.CompletedAt is not { } completedAt)
        {
            throw new ArgumentException("Only a completed run can update conversation history.", nameof(run));
        }
        if (run.BehaviorId != "intochat" || run.Input.ValueKind != JsonValueKind.Object
            || !run.Input.TryGetProperty("threadId", out var owner) || owner.GetString() != this.GetPrimaryKeyString())
        {
            throw new ArgumentException("This run does not belong to this conversation.", nameof(run));
        }

        await ReloadIfRequiredAsync();
        if (history.State.LastRunId == run.RunId || history.State.CompletedAt is { } previous && completedAt <= previous)
        {
            return;
        }

        List<ChatMessage> messages;
        if (run.Output.ValueKind == JsonValueKind.Object && run.Output.TryGetProperty("history", out var canonical)
            && canonical.ValueKind == JsonValueKind.Array)
        {
            messages = BehaviorConversationHistory.Read(canonical);
        }
        else
        {
            messages = BehaviorConversationHistory.Read(run.Input.GetProperty("messages"));
            messages.Add(new ChatMessage(ChatRole.Assistant, BehaviorConversationHistory.Answer(run.Output)));
        }
        BehaviorConversationHistory.Trim(messages, 16_000);
        await SaveAsync(new(run.RunId, completedAt, JsonSerializer.Serialize(messages, Json)));
    }

    private async Task SaveAsync(ConversationState updated)
    {
        history.State = updated;
        // A failed/ambiguous write must be re-read before any later request uses this state.
        _reloadRequired = true;
        await history.WriteStateAsync();
        _reloadRequired = false;
    }

    private async Task ReloadIfRequiredAsync()
    {
        if (_reloadRequired)
        {
            await history.ReadStateAsync();
            _reloadRequired = false;
        }
    }
}

[GenerateSerializer, Alias("intochat.conversation-state")]
internal sealed record ConversationState(
    [property: Id(0)] string? LastRunId,
    [property: Id(1)] DateTimeOffset? CompletedAt,
    [property: Id(2)] string MessagesJson)
{
    public ConversationState() : this(null, null, "[]") { }
}
