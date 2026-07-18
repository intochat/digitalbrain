using Brain.Contracts;
using DigitalBrain.AI;

namespace Brain.Gateway;

public sealed class GroupChatSurfaceOwner(IGroupChat groupChat) : ISurfaceOwner
{
    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        groupChat.ApplyUiActionAsync(command);

    public Task<UiSurfaceSnapshot> GetSurfaceAsync() => groupChat.GetSurfaceAsync();
}
