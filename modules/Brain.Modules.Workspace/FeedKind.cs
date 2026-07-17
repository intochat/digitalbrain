using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Workspace;

public sealed class FeedKind : INeuronKind
{
    public string Kind => "feed";
    public string[] Contracts => ["feed.append.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "feed.append.v1" => HandleAppendAsync(context, invocation),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    private ValueTask<KindResult> HandleAppendAsync(NeuronContext context, NeuronInvocation invocation)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(invocation.InputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("sourceKey", out var sourceKeyElement) || sourceKeyElement.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "sourceKey field is required");

            var sourceKey = sourceKeyElement.GetString();
            if (string.IsNullOrEmpty(sourceKey))
                throw new BrainException("input.invalid", "sourceKey cannot be empty");

            if (!root.TryGetProperty("revision", out var revisionElement) ||
                revisionElement.ValueKind != JsonValueKind.Number ||
                !revisionElement.TryGetDouble(out var revisionValue) ||
                revisionValue <= 0)
                throw new BrainException("input.invalid", "revision must be a positive number");

            if (!root.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "kind field is required");

            var kind = kindElement.GetString();
            if (string.IsNullOrEmpty(kind))
                throw new BrainException("input.invalid", "kind cannot be empty");

            var eventPayload = JsonSerializer.Serialize(new { sourceKey, revision = revisionElement, kind });

            var output = JsonSerializer.Serialize(new { sequence = context.Revision + 1 });
            var events = new[] { ("feed.record", eventPayload) };

            return ValueTask.FromResult(new KindResult(output, events));
        }
    }

    public string Project(NeuronContext context, string projection)
    {
        var records = context.Journal
            .Where(evt => evt.Kind == "feed.record")
            .Reverse()
            .Take(50)
            .Select(evt =>
            {
                using var recordDoc = JsonDocument.Parse(evt.PayloadJson);
                return recordDoc.RootElement.Clone();
            })
            .ToArray();

        return JsonSerializer.Serialize(new { records });
    }
}
