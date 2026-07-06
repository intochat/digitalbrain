using DigitalBrain.Core;

namespace DigitalBrain.Telegram;

[Alias("DigitalBrain.Telegram.ITelegramChatNeuron")]
public interface ITelegramChatNeuron : IChannelNeuron
{
    [Alias("GetBoundBundleAsync")]
    Task<string?> GetBoundBundleAsync();
}
