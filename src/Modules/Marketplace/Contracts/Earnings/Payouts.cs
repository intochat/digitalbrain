namespace DigitalBrain.Marketplace;

public enum CountrySupport
{
    Supported = 0,
    Unsupported = 1,
}

[GenerateSerializer, Alias("marketplace.screening-request")]
public sealed record ScreeningRequest
{
    [Id(0)] public required string CreatorId { get; init; }
    [Id(1)] public required string LegalName { get; init; }
    [Id(2)] public required string Country { get; init; }
}

[GenerateSerializer, Alias("marketplace.screening-result")]
public sealed record ScreeningResult
{
    [Id(0)] public required bool Cleared { get; init; }
    [Id(1)] public string? Reason { get; init; }
}

// Sanctions screening is an adapter: the platform never talks to a list provider directly, and
// the registered adapter is a deterministic fake until G-1/G-3 clear a real one.
public interface ISanctionsScreener
{
    Task<ScreeningResult> ScreenAsync(ScreeningRequest request, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("marketplace.onboarding-request")]
public sealed record OnboardingRequest
{
    [Id(0)] public required string CreatorId { get; init; }
    [Id(1)] public required string LegalName { get; init; }
    [Id(2)] public required string Country { get; init; }
}

// The verified identity, supported country and tax form a payout needs. A creator who is not
// payout-ready can still publish free apps.
[GenerateSerializer, Alias("marketplace.creator-onboarding")]
public sealed record CreatorOnboarding
{
    [Id(0)] public required string CreatorId { get; init; }
    [Id(1)] public required bool IdentityVerified { get; init; }
    [Id(2)] public required bool TaxFormOnFile { get; init; }
    [Id(3)] public required CountrySupport Country { get; init; }
    [Id(4)] public required bool SanctionsCleared { get; init; }
    [Id(5)] public string? Reason { get; init; }
    [Id(6)] public string? CountryCode { get; init; }
    [Id(7)] public string? LegalName { get; init; }

    public bool CanReceivePayouts => IdentityVerified && TaxFormOnFile && Country == CountrySupport.Supported && SanctionsCleared;

    public bool CanPublishPaidApps => CanReceivePayouts;
}

// Identity, country, tax-form and sanctions checks behind one adapter, with a deterministic fake.
public interface ICreatorOnboardingProvider
{
    Task<CreatorOnboarding> OnboardAsync(OnboardingRequest request, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("marketplace.connect-account-request")]
public sealed record ConnectAccountRequest
{
    [Id(0)] public required string CreatorId { get; init; }
    [Id(1)] public required string Country { get; init; }
    [Id(2)] public required string LegalName { get; init; }
}

[GenerateSerializer, Alias("marketplace.connect-account")]
public sealed record ConnectAccount
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required bool PayoutsEnabled { get; init; }
}

[GenerateSerializer, Alias("marketplace.payout-transfer-request")]
public sealed record PayoutTransferRequest
{
    [Id(0)] public required string AccountId { get; init; }
    [Id(1)] public required string PayoutId { get; init; }
    [Id(2)] public required decimal AmountUsd { get; init; }
    [Id(3)] public required string Currency { get; init; }
}

[GenerateSerializer, Alias("marketplace.payout-transfer-result")]
public sealed record PayoutTransferResult
{
    [Id(0)] public required bool Succeeded { get; init; }
    [Id(1)] public required string ProviderReference { get; init; }
    [Id(2)] public string? FailureReason { get; init; }
}

// Shaped like Stripe Connect: a connected account per creator and a transfer per payout. Gate G-2
// forbids calling Stripe, so the registered adapter is the deterministic fake.
public interface IPayoutProvider
{
    Task<ConnectAccount> CreateConnectedAccountAsync(ConnectAccountRequest request, CancellationToken cancellationToken = default);

    Task<PayoutTransferResult> TransferAsync(PayoutTransferRequest request, CancellationToken cancellationToken = default);
}
