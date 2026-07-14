using DigitalBrain.Kernel.Runtime;
using Orleans;

namespace DigitalBrain.Salesforce;

public interface ISalesforceMutationGateway
{
    Task<SalesforceMutationPreviewResult> PreviewAsync(
        string actorScope,
        SalesforceUpdatePreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<SalesforceMutationApplyResult> ApplyAsync(
        string actorScope,
        SalesforcePreparedUpdate preparedUpdate,
        CancellationToken cancellationToken = default);

    Task<SalesforceMutationVerificationResult> VerifyAsync(
        string actorScope,
        SalesforcePreparedUpdate preparedUpdate,
        CancellationToken cancellationToken = default);
}

public sealed class SalesforceMutationGateway(IGrainFactory grainFactory) : ISalesforceMutationGateway
{
    public Task<SalesforceMutationPreviewResult> PreviewAsync(
        string actorScope,
        SalesforceUpdatePreviewRequest request,
        CancellationToken cancellationToken = default) =>
        Grain(actorScope).PreviewUpdateAsync(request, cancellationToken);

    public Task<SalesforceMutationApplyResult> ApplyAsync(
        string actorScope,
        SalesforcePreparedUpdate preparedUpdate,
        CancellationToken cancellationToken = default) =>
        Grain(actorScope).ApplyUpdateAsync(preparedUpdate, cancellationToken);

    public Task<SalesforceMutationVerificationResult> VerifyAsync(
        string actorScope,
        SalesforcePreparedUpdate preparedUpdate,
        CancellationToken cancellationToken = default) =>
        Grain(actorScope).VerifyUpdateAsync(preparedUpdate, cancellationToken);

    private ISalesforceMutationToolGrain Grain(string actorScope) =>
        grainFactory.GetGrain<ISalesforceMutationToolGrain>(actorScope);
}
