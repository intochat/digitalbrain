using System.Text.RegularExpressions;

namespace Brain.Abstractions.Contracts;

public readonly partial record struct ContractId
{
    public ContractId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        if (!CanonicalPattern().IsMatch(value))
        {
            throw new ArgumentException(
                "A contract id must use the canonical 'module/name@major' syntax.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9-]*/[a-z][a-z0-9-]*@[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalPattern();
}
