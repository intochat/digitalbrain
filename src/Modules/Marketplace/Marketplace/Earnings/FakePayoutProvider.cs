namespace DigitalBrain.Marketplace;

// Gate G-2 blocks a real Stripe Connect integration until written confirmation is on file, so the
// registered payout provider is this deterministic fake. It has the Connect shape: a connected
// account per creator and a transfer per payout. It never opens a network connection.
internal sealed class FakePayoutProvider : IPayoutProvider
{
    internal static readonly FakePayoutProvider Instance = new();

    public Task<ConnectAccount> CreateConnectedAccountAsync(ConnectAccountRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new ConnectAccount
        {
            AccountId = $"acct_fake_{request.CreatorId}",
            PayoutsEnabled = true,
        });
    }

    public Task<PayoutTransferResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new PayoutTransferResult
        {
            Succeeded = true,
            ProviderReference = $"payout_fake_{request.PayoutId}",
        });
    }
}
