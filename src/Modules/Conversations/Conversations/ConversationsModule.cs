using DigitalBrain.Core;

namespace DigitalBrain.Conversations;

public sealed class ConversationsModule : IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
