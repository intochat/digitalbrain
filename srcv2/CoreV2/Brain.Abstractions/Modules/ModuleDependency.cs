using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Modules;

public readonly record struct ModuleVersion : IComparable<ModuleVersion>
{
    public ModuleVersion(int major, int minor, int patch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
        ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
        ArgumentOutOfRangeException.ThrowIfNegative(patch, nameof(patch));

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public int CompareTo(ModuleVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record ModuleDependency
{
    public ModuleDependency(
        ModuleId module,
        ModuleVersion minimumInclusive,
        ModuleVersion maximumExclusive)
    {
        if (string.IsNullOrWhiteSpace(module.Value))
        {
            throw new ArgumentException("A module dependency requires a module id.", nameof(module));
        }

        if (minimumInclusive.CompareTo(maximumExclusive) >= 0)
        {
            throw new ArgumentException(
                "A module dependency range must have a lower bound before its upper bound.",
                nameof(maximumExclusive));
        }

        Module = module;
        MinimumInclusive = minimumInclusive;
        MaximumExclusive = maximumExclusive;
    }

    public ModuleId Module { get; }

    public ModuleVersion MinimumInclusive { get; }

    public ModuleVersion MaximumExclusive { get; }

    public bool Accepts(ModuleVersion version)
        => version.CompareTo(MinimumInclusive) >= 0 && version.CompareTo(MaximumExclusive) < 0;
}
