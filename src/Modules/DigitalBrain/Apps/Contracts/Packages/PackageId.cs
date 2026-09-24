using System.Text.RegularExpressions;

namespace DigitalBrain.Apps;

// "owner/name": the owner is the publishing account's username.
[GenerateSerializer, Alias("apps.package-id")]
public sealed partial record PackageId([property: Id(0)] string Owner, [property: Id(1)] string Name)
{
    public static PackageId Parse(string value)
    {
        var parts = (value ?? "").Split('/');
        return parts is [var owner, var name]
            ? Create(owner, name)
            : throw new ArgumentException("A package is addressed as owner/name.", nameof(value));
    }

    public static PackageId Create(string owner, string name)
    {
        if (!OwnerPattern().IsMatch(owner ?? "")) { throw new ArgumentException("A package owner is a lowercase username of letters, numbers and hyphens.", nameof(owner)); }
        if (!NamePattern().IsMatch(name ?? "")) { throw new ArgumentException("A package name is 1-64 lowercase letters, numbers and hyphens.", nameof(name)); }
        return new(owner!, name!);
    }

    public override string ToString() => Owner + "/" + Name;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex NamePattern();
}
