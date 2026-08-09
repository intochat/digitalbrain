using System.Text.RegularExpressions;

namespace DigitalBrain.Poc.Runtime;

public readonly partial record struct CandidateFamilyId
{
    private CandidateFamilyId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CandidateFamilyId Parse(string value)
    {
        if (value is null || !FamilyPattern().IsMatch(value))
        {
            throw new FormatException(
                "A candidate family identifier must use cf_ followed by 26 lowercase base32 characters.");
        }

        return new CandidateFamilyId(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^cf_[a-z2-7]{26}$", RegexOptions.CultureInvariant)]
    private static partial Regex FamilyPattern();
}
