using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

public static class ChatButtons
{
    public static string OfferedInstanceName(string chatName, CommandId offer, string buttonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonId);

        return $"{chatName}.{offer.Value:D}.{buttonId}";
    }

    public static Guid ArmingConnectionId(NeuronId button)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(button.ToString())).AsSpan(0, 16));
}
