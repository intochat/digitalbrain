namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed class CandidateManifest(IReadOnlyCollection<string> aliases)
{
    public string Contract(string alias) => aliases.Contains(alias, StringComparer.Ordinal)
        ? alias
        : throw new KeyNotFoundException($"Fixture manifest has no contract '{alias}'.");
}
