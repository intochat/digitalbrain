using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

public static class ChatRoles
{
    public const string Responder = "role:responder";

    public static Guid ResponderConnectionId(NeuronId chat)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Responder}|{chat}")).AsSpan(0, 16));
}
