using Microsoft.Extensions.AI;
using Orleans.Serialization;

namespace DigitalBrain.AI;

internal static class ChatMessageCopies
{
    internal static ChatMessage[] Clone(
        IEnumerable<ChatMessage> messages,
        Serializer<ChatMessage> serializer)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(serializer);

        return
        [
            .. messages.Select(message => serializer.Deserialize(
                serializer.SerializeToArray(message
                    ?? throw new InvalidOperationException("A mapped chat message cannot be null."))))
        ];
    }
}
