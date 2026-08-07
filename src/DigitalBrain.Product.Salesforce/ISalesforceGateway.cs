namespace DigitalBrain.Product.Salesforce;

public interface ISalesforceGateway
{
    /// <summary>
    /// Applies the frozen mutation or reconciles a prior attempt. Implementations
    /// must use <see cref="PreparedAccountDescriptionMutation.MutationId"/> as
    /// the immutable idempotency key so repeated calls converge on one logical
    /// external mutation, including after local journal-recording recovery.
    /// </summary>
    Task<SalesforceGatewayOutcome> ApplyOrReconcileAsync(
        PreparedAccountDescriptionMutation mutation,
        CancellationToken cancellationToken);
}
