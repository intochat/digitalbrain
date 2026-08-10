using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

public static class ChatButtons
{
    // An unclicked offer's click route must not accumulate forever; a day is
    // long enough for any live conversation to act on it.
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromHours(24);

    public static string OfferedInstanceName(string chatName, CommandId offer, string buttonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonId);

        return $"{chatName}.{offer.Value:D}.{buttonId}";
    }

    public static Guid ArmingConnectionId(NeuronId button)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(button.ToString())).AsSpan(0, 16));
}
