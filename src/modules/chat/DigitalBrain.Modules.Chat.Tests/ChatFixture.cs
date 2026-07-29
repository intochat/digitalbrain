using DigitalBrain.AI;
using DigitalBrain.Testing;

namespace DigitalBrain.Chat.Tests;

public sealed class ChatFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
    }
}
