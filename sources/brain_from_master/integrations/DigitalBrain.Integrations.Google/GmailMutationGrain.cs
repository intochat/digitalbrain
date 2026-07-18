using System.Net;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Configuration;
using Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace DigitalBrain.Integrations.Google;

[GrainType("digitalbrain.google.gmail-mutation")]
internal sealed class GmailMutationGrain(
    ILogger<GmailMutationGrain> logger,
    IGmailApiClientFactory clients,
    IIntegrationConfigStore store,
    [FromKeyedServices("google")] IConnector connector) : Grain, IGmailMutationToolGrain
{
    public async Task<GmailSendResult> SendAsync(GmailSendRequest request, CancellationToken cancellationToken = default)
    {
        if (!GmailSendRequestValidator.IsValid(request))
            return new(GmailSendStatus.InvalidRequest, SafeReason: "The Gmail message is invalid.");
        var scope = new NeuronScope(new UserId(this.GetPrimaryKeyString()), null);
        if (!(await connector.ValidateConfigAsync(cancellationToken: cancellationToken)).IsValid)
            return new(GmailSendStatus.ConfigurationMissing, SafeReason: "Gmail is not configured.");
        var values = await GoogleClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!GoogleClientFactory.HasUsableCredential(values))
            return new(GmailSendStatus.NeedsAuth, SafeReason: "Reconnect Gmail before sending email.");
        try
        {
            return await (await clients.CreateAsync(scope, cancellationToken)).SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new(GmailSendStatus.NeedsAuth, SafeReason: "Reconnect Gmail before sending email.");
        }
        catch (Exception exception)
        {
            logger.LogWarning("Gmail send failed with {ExceptionType}.", exception.GetType().Name);
            return new(GmailSendStatus.Unavailable, SafeReason: "The Gmail send outcome is unavailable.");
        }
    }
}
