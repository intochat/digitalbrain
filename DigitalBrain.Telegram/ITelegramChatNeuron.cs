using DigitalBrain.Core;

namespace DigitalBrain.Telegram;

public interface ITelegramChatNeuron : IChannelNeuron
{
    Task<string?> GetBoundBundleAsync();
}
