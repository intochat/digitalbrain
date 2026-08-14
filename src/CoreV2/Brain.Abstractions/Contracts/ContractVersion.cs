namespace Brain.Abstractions.Contracts;

public readonly record struct ContractVersion
{
    public ContractVersion(int major)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(major, nameof(major));
        Major = major;
    }

    public int Major { get; }

    public override string ToString() => Major.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
