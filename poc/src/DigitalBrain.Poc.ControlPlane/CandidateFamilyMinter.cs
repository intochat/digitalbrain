using System.Security.Cryptography;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class CandidateFamilyMinter
{
    private const int EncodedLength = 26;
    private readonly IBase32Source _source;
    private readonly ICandidateFamilyRegistry _families;

    public CandidateFamilyMinter()
        : this(new CryptographicBase32Source(), UnconfiguredRegistry.Instance)
    {
    }

    public CandidateFamilyMinter(ICandidateFamilyRegistry families)
        : this(new CryptographicBase32Source(), families)
    {
    }

    public CandidateFamilyMinter(IBase32Source source, ICandidateFamilyRegistry families)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _families = families ?? throw new ArgumentNullException(nameof(families));
    }

    public CandidateFamilyId Mint() =>
        CandidateFamilyId.Parse($"cf_{_source.Next(EncodedLength)}");

    public async Task<CandidateFamilyId> MintAndReserveAsync(
        AuthenticatedPrincipal owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Mint();
            if (await _families.TryReserveAsync(owner, candidate, cancellationToken))
            {
                return candidate;
            }
        }
    }

    private sealed class CryptographicBase32Source : IBase32Source
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public string Next(int length)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            Span<byte> bytes = stackalloc byte[length];
            RandomNumberGenerator.Fill(bytes);
            Span<char> result = stackalloc char[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = Alphabet[bytes[index] & 31];
            }

            return new string(result);
        }
    }

    private sealed class UnconfiguredRegistry : ICandidateFamilyRegistry
    {
        public static UnconfiguredRegistry Instance { get; } = new();

        public ValueTask<bool> TryReserveAsync(
            AuthenticatedPrincipal owner,
            CandidateFamilyId family,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "MintAndReserveAsync requires an explicitly configured trusted candidate-family registry.");

        public ValueTask<bool> IsReservedForAsync(
            AuthenticatedPrincipal owner,
            CandidateFamilyId family,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Reservation lookup requires an explicitly configured trusted candidate-family registry.");
    }
}
