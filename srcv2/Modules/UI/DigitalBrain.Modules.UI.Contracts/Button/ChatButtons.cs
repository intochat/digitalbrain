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
}
