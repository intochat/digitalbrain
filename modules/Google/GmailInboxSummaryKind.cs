using System.Text;
using System.Text.Json;
using AI.Contracts;
using Brain.Contracts;
using Flutter.Contracts;
using Google.Contracts;

namespace Brain.Modules.Google;

public sealed class GmailInboxSummaryKind(IGrainFactory grainFactory) : INeuronKind
{
    private const int MaximumPromptBytes = 32_768;
    private const string SummaryInstruction =
        "Summarize this inbox clearly and concisely. Identify important items and actions without inventing facts.";
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public string Kind => "gmail-assistant";
    public string[] Contracts => [GoogleCapabilityIds.GmailInboxSummarize];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            GoogleCapabilityIds.GmailInboxSummarize => SummarizeAsync(context, invocation),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection)
    {
        var summaries = context.Journal.Count(entry => entry.Kind == "gmail.inbox-summarized");
        return JsonSerializer.Serialize(new { summaries }, JsonOptions);
    }

    private async ValueTask<KindResult> SummarizeAsync(
        NeuronContext context,
        NeuronInvocation invocation)
    {
        var request = ParseRequest(invocation.InputJson);
        var callerKey = context.Address.ToGrainKey();
        var gmailAddress = Address(context, "gmail/assistant");
        var gmail = grainFactory.GetGrain<INeuron>(gmailAddress);

        var mailboxReceipt = await gmail.InvokeAsync(new(
            GoogleCapabilityIds.GmailMailboxRead,
            JsonSerializer.Serialize(new GmailMailboxReadRequest(request.MaximumMessages), JsonOptions),
            ChildCommand(invocation.CommandId, "mailbox"),
            callerKey));
        var mailbox = Deserialize<GmailMailboxPage>(mailboxReceipt.OutputJson);

        var prompt = new StringBuilder();
        var remainingBytes = MaximumPromptBytes
            - Encoding.UTF8.GetByteCount(SummaryInstruction)
            - 1; // IChatClient receives the system and user messages separated by a newline.
        var messageCount = 0;
        foreach (var summary in mailbox.Messages.Take(request.MaximumMessages))
        {
            var messageReceipt = await gmail.InvokeAsync(new(
                GoogleCapabilityIds.GmailMessageRead,
                JsonSerializer.Serialize(new GmailMessageReadRequest(summary.MessageId), JsonOptions),
                ChildCommand(invocation.CommandId, $"message-{messageCount}"),
                callerKey));
            var message = Deserialize<GmailMessage>(messageReceipt.OutputJson);
            messageCount++;

            var entry = $"""
                Message {messageCount}
                From: {message.SenderAddress ?? "(unknown)"}
                Subject: {message.Subject ?? "(none)"}
                Body:
                {message.PlainTextBody}

                """;
            if (!AppendBounded(prompt, entry, ref remainingBytes))
                break;
        }

        if (prompt.Length == 0)
            prompt.Append("No messages were returned.");

        var ai = grainFactory.GetGrain<INeuron>(Address(context, "llm/balanced"));
        var generationReceipt = await ai.InvokeAsync(new(
            AiCapabilityIds.TextGenerate,
            JsonSerializer.Serialize(
                new TextGenerationRequest(SummaryInstruction, prompt.ToString()),
                JsonOptions),
            ChildCommand(invocation.CommandId, "summary"),
            callerKey));
        var generated = Deserialize<TextGenerationResult>(generationReceipt.OutputJson);
        var boundedSummary = TruncateUtf8(generated.Text, UiDocument.MaximumTextLength);

        var blocks = new List<UiBlock>
        {
            new("heading", Text: "Inbox summary"),
            new("text", Text: boundedSummary),
            new("status", Label: "Messages", Value: messageCount.ToString())
        };
        if (request.Reply is not null)
        {
            blocks.Add(new UiBlock(
                "button",
                Label: "Review reply",
                Action: new UiAction(
                    GoogleCapabilityIds.GmailSendPropose,
                    gmailAddress,
                    JsonSerializer.Serialize(request.Reply, JsonOptions))));
        }

        var window = grainFactory.GetGrain<INeuron>(Address(context, "window/main"));
        var windowReceipt = await window.InvokeAsync(new(
            "window.render.v1",
            JsonSerializer.Serialize(
                new UiDocument(UiDocument.CurrentVersion, blocks),
                JsonOptions),
            ChildCommand(invocation.CommandId, "window"),
            callerKey));
        var windowReply = Deserialize<WindowReply>(windowReceipt.OutputJson);

        var output = new GmailInboxSummaryReceipt(
            messageCount,
            boundedSummary,
            windowReply.Revision);
        var eventPayload = JsonSerializer.Serialize(new
        {
            messageCount,
            windowRevision = windowReply.Revision
        }, JsonOptions);
        return new KindResult(
            JsonSerializer.Serialize(output, JsonOptions),
            [("gmail.inbox-summarized", eventPayload)]);
    }

    private static GmailInboxSummaryRequest ParseRequest(string inputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<GmailInboxSummaryRequest>(inputJson, JsonOptions)
                ?? throw new BrainException("input.invalid", "request is required");
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed or invalid request");
        }
        catch (ArgumentException exception)
        {
            throw new BrainException("input.invalid", exception.Message);
        }
    }

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new BrainException("result.invalid", $"{typeof(T).Name} result is required");

    private static string Address(NeuronContext context, string neuronId) =>
        new NeuronAddress(context.Address.OwnerId, context.Address.SpaceId, neuronId).ToGrainKey();

    private static string ChildCommand(string commandId, string step) =>
        $"{commandId}:{step}";

    private static bool AppendBounded(
        StringBuilder builder,
        string value,
        ref int remainingBytes)
    {
        if (remainingBytes <= 0)
            return false;

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= remainingBytes)
        {
            builder.Append(value);
            remainingBytes -= bytes.Length;
            return true;
        }

        var length = remainingBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            length--;
        builder.Append(Encoding.UTF8.GetString(bytes, 0, length));
        remainingBytes = 0;
        return false;
    }

    private static string TruncateUtf8(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];
}
