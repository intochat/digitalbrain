using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Salesforce;

namespace Brain.Gateway;

public sealed class SalesforceSurfaceOwner(ISalesforce salesforce) : ISurfaceOwner
{
    public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
        throw new InvalidOperationException("Salesforce surface does not accept UiAction commands.");

    public Task<UiSurfaceSnapshot> GetSurfaceAsync() => salesforce.GetSurfaceAsync();
}
