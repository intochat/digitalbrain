using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Salesforce;

[GrainType("digitalbrain.salesforce.mutation")]
public sealed class SalesforceMutationNeuron(
    ILogger<SalesforceMutationNeuron> logger,
    ISalesforceApiClientFactory salesforceApiClientFactory,
    IPackConfigStore store,
    [FromKeyedServices("salesforce")] IConnector connector)
    : Grain, ISalesforceMutationToolGrain
{
    public async Task<SalesforceMutationPreviewResult> PreviewUpdateAsync(
        SalesforceUpdatePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await CreateClientAsync(cancellationToken);
            if (connection.Status is { } status)
                return new SalesforceMutationPreviewResult(status, SafeReason: SafeReason(status));
            return await connection.Client!.PreviewUpdateAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Principal-scoped Salesforce update preview failed with {ExceptionType}.",
                ex.GetType().Name);
            return new SalesforceMutationPreviewResult(
                SalesforceMutationStatus.Unavailable,
                SafeReason: SafeReason(SalesforceMutationStatus.Unavailable));
        }
    }

    public async Task<SalesforceMutationApplyResult> ApplyUpdateAsync(
        SalesforcePreparedUpdate preparedUpdate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await CreateClientAsync(cancellationToken);
            if (connection.Status is { } status)
                return new SalesforceMutationApplyResult(status, SafeReason(status));
            return await connection.Client!.ApplyUpdateAsync(preparedUpdate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Principal-scoped Salesforce update apply failed with {ExceptionType}.",
                ex.GetType().Name);
            return new SalesforceMutationApplyResult(
                SalesforceMutationStatus.Unavailable,
                SafeReason(SalesforceMutationStatus.Unavailable));
        }
    }

    private async Task<(SalesforceMutationStatus? Status, ISalesforceApiClient? Client)> CreateClientAsync(
        CancellationToken cancellationToken)
    {
        var owner = new NeuronId(this.GetPrimaryKeyString());
        var scope = new NeuronScope(new UserId(owner.Value), ThreadId: null);
        var config = await connector.ValidateConfigAsync(
            PackConfigScopes.ForUser(scope.UserId),
            cancellationToken);
        if (!config.IsValid)
            return (SalesforceMutationStatus.ConfigurationMissing, null);

        var values = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope, cancellationToken);
        if (!SalesforceClientFactory.HasUsableCredential(values))
            return (SalesforceMutationStatus.NeedsAuth, null);
        return (null, await salesforceApiClientFactory.CreateAsync(scope, cancellationToken));
    }

    private static string SafeReason(SalesforceMutationStatus status) => status switch
    {
        SalesforceMutationStatus.ConfigurationMissing => "Salesforce application configuration is missing.",
        SalesforceMutationStatus.NeedsAuth => "Connect Salesforce before applying this update.",
        _ => "Salesforce updates are unavailable right now."
    };
}
