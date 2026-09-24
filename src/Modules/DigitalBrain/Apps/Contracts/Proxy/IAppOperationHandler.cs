namespace DigitalBrain.Apps;

// A platform-registered handler for an app operation. Apps never ship grain types; the platform
// maps their declared operations onto neurons it already trusts.
public interface IAppOperationHandler
{
    ValueTask<bool> InvokeAsync(AppProxyRequest request, CancellationToken cancellationToken = default);
}
