namespace DigitalBrain.Marketplace;

// Verified identity, a supported country and a tax form are the payout prerequisites. The fake is
// deterministic on the request, so tests cover both a payout-ready creator and one that cannot be
// onboarded (that creator may still publish free apps).
internal sealed class FakeCreatorOnboardingProvider(ISanctionsScreener screener) : ICreatorOnboardingProvider
{
    internal static readonly string[] SupportedCountries =
    [
        "US", "GB", "DE", "FR", "NL", "ES", "IT", "IE", "CA", "AU",
        "SE", "PL", "PT", "AT", "BE", "DK", "FI", "NO", "CH",
    ];

    public async Task<CreatorOnboarding> OnboardAsync(OnboardingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var identityVerified = !string.IsNullOrWhiteSpace(request.LegalName);
        var taxFormOnFile = identityVerified;
        var country = SupportedCountries.Contains(request.Country, StringComparer.OrdinalIgnoreCase)
            ? CountrySupport.Supported
            : CountrySupport.Unsupported;
        var screening = await screener.ScreenAsync(
            new ScreeningRequest { CreatorId = request.CreatorId, LegalName = request.LegalName, Country = request.Country },
            cancellationToken).ConfigureAwait(true);

        var failures = new List<string>();
        if (!identityVerified) { failures.Add("identity is not verified"); }
        if (!taxFormOnFile) { failures.Add("tax form is not on file"); }
        if (country == CountrySupport.Unsupported) { failures.Add($"country {request.Country} is not supported"); }
        if (!screening.Cleared) { failures.Add(screening.Reason ?? "sanctions screening did not clear"); }

        return new CreatorOnboarding
        {
            CreatorId = request.CreatorId,
            IdentityVerified = identityVerified,
            TaxFormOnFile = taxFormOnFile,
            Country = country,
            SanctionsCleared = screening.Cleared,
            Reason = failures.Count == 0 ? null : string.Join("; ", failures),
            CountryCode = request.Country,
            LegalName = request.LegalName,
        };
    }
}
