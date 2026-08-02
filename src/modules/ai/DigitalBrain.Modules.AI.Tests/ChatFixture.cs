using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Testing;

namespace DigitalBrain.ModuleTests;

public sealed class ChatFixture : DigitalBrainFixture
{
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.WithResponseTimeout(ResponseTimeout);
    }
}
