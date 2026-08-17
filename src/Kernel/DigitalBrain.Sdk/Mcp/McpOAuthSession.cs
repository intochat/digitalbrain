using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal sealed class McpOAuthSession
{
    private readonly CancellationTokenSource _lifetime = new();

    internal McpOAuthSession(
        CommandId commandId,
        string serverKey,
        string serverDisplayName,
        OwnerId owner,
        IGrainFactory grains,
        ActorContext? actor = null)
    {
        CommandId = commandId;
        ServerKey = serverKey;
        ServerDisplayName = serverDisplayName;
        Owner = owner;
        Grains = grains;
        Actor = actor;
    }

    internal CommandId CommandId { get; }
    internal string ServerKey { get; }
    internal string ServerDisplayName { get; }
    internal OwnerId Owner { get; }
    internal IGrainFactory Grains { get; }
    internal ActorContext? Actor { get; }
    internal CancellationToken Cancellation => _lifetime.Token;

    internal void Cancel()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
