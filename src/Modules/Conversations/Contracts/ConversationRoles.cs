using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Conversations;

public static class ConversationRoles
{
    public const string Responder = "role:responder";

    public static Guid ResponderConnectionId(NeuronId conversation)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Responder}|{conversation}")).AsSpan(0, 16));
}
