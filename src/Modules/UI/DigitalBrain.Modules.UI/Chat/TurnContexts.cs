using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal static class TurnContexts
{
    internal static TurnContext For(SignalId turn, string text, IReadOnlyList<ContextRef>? refs, IReadOnlyList<TurnContext> contexts)
    {
        List<ContextSlot> slots = [];
        var messagePath = $"chat.turn.{turn}";
        foreach (var reference in refs ?? [])
        {
            var related = contexts.FirstOrDefault(context => context.Turn != turn && reference.Path == $"chat.turn.{context.Turn}");
            if (related is not null)
            {
                foreach (var slot in related.Slots)
                {
                    Contribute(slots, slot);
                }
            }
            else
            {
                Contribute(slots, Slot(reference));
            }
        }

        // The current message always occupies the final position, even if a ref used its path.
        slots.RemoveAll(slot => slot.Path == messagePath);
        slots.Add(Slot(new ContextRef(messagePath, "chat.message.v1", ChatBodies.Text(text))));
        return new TurnContext(turn, slots);
    }

    private static void Contribute(List<ContextSlot> slots, ContextSlot slot)
    {
        var index = slots.FindIndex(existing => existing.Path == slot.Path);
        if (index < 0)
        {
            slots.Add(slot);
        }
        else
        {
            slots[index] = slot;
        }
    }

    // The digest recipe is the Execution module's, byte for byte: it must keep naming the same content.
    private static ContextSlot Slot(ContextRef reference)
    {
        var material = string.Concat(reference.SchemaHash, "\0", reference.PayloadJson ?? string.Empty, "\0", reference.BlobRef ?? string.Empty);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return new ContextSlot(reference.Path, reference.SchemaHash, reference.PayloadJson, reference.BlobRef, digest);
    }

    internal static async Task RetainAsync(List<TurnContext> contexts, ITurnContextBlobStore store, CancellationToken cancellationToken)
    {
        for (var index = 0; index < contexts.Count - ChatState.MaxInlineContexts; index++)
        {
            List<ContextSlot> slots = [];
            foreach (var slot in contexts[index].Slots)
            {
                slots.Add(slot.PayloadJson is { } payload
                    ? slot with { PayloadJson = null, BlobRef = await store.SaveAsync(slot.Digest, payload, cancellationToken).ConfigureAwait(true) }
                    : slot);
            }

            contexts[index] = contexts[index] with { Slots = slots };
        }
    }

    internal static async Task<string> PreambleAsync(TurnContext context, ITurnContextBlobStore store, CancellationToken cancellationToken)
    {
        var preamble = new StringBuilder();
        foreach (var slot in context.Slots.Where(slot => slot.Path != $"chat.turn.{context.Turn}"))
        {
            var payload = slot.PayloadJson ?? (slot.BlobRef is { } blobRef
                ? await store.ReadAsync(blobRef, cancellationToken).ConfigureAwait(true)
                : null);
            preamble.Append($"Context {slot.Path} ({slot.SchemaHash}): {payload}\n");
        }

        return preamble.ToString();
    }
}
