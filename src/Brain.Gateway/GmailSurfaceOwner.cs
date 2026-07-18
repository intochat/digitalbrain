using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Google;

namespace Brain.Gateway;

public sealed class GmailSurfaceOwner(IGmail gmail) : ISurfaceOwner
{
    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        throw new InvalidOperationException("Gmail surface does not accept UiAction commands.");

    public Task<UiSurfaceSnapshot> GetSurfaceAsync() => gmail.GetSurfaceAsync();
}
