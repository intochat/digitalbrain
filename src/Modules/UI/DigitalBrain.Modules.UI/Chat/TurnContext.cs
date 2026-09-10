using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.turn-context")]
public sealed record TurnContext(
    [property: Id(0)] SignalId Turn,
    [property: Id(1)] IReadOnlyList<ContextSlot> Slots);

[GenerateSerializer, Alias("db.ui.context-slot")]
public sealed record ContextSlot(
    [property: Id(0)] string Path,
    [property: Id(1)] string SchemaHash,
    [property: Id(2)] string? PayloadJson,
    [property: Id(3)] string? BlobRef,
    [property: Id(4)] string Digest,
    [property: Id(5)] int Ordinal);

internal static class TurnContexts
{
    internal static TurnContext For(SignalId turn, string text, IReadOnlyList<ContextRef>? refs)
    {
        var message = new ContextRef($"chat.turn.{turn}", "chat.message.v1", ChatBodies.Text(text));
        List<ContextSlot> slots = [];
        foreach (var reference in new[] { message }.Concat(refs ?? []))
        {
            var index = slots.FindIndex(slot => slot.Path == reference.Path);
            var ordinal = index < 0 ? slots.Count : index;
            var slot = new ContextSlot(reference.Path, reference.SchemaHash, reference.PayloadJson, reference.BlobRef,
                Digest(reference.SchemaHash, reference.PayloadJson, reference.BlobRef), ordinal);
            if (index < 0)
            {
                slots.Add(slot);
            }
            else
            {
                slots[index] = slot;
            }
        }

        return new TurnContext(turn, slots);
    }

    internal static string Digest(string schemaHash, string? payloadJson, string? blobRef)
    {
        var material = string.Concat(schemaHash, "\0", payloadJson ?? string.Empty, "\0", blobRef ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    internal static void Retain(List<TurnContext> contexts)
    {
        for (var index = 0; index < contexts.Count - ChatState.MaxInlineContexts; index++)
        {
            contexts[index] = contexts[index] with
            {
                Slots = [.. contexts[index].Slots.Select(slot => slot with { PayloadJson = null })],
            };
        }
    }

    internal static string Preamble(TurnContext context)
        => string.Concat(context.Slots.Skip(1).Select(slot => $"Context {slot.Path} ({slot.SchemaHash}): {slot.PayloadJson ?? slot.BlobRef}\n"));
}
