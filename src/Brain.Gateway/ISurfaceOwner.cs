using Brain.Contracts;
using DigitalBrain.AI;

namespace Brain.Gateway;

public interface ISurfaceOwner
{
    Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command);
    Task<UiSurfaceSnapshot> GetSurfaceAsync();
}
