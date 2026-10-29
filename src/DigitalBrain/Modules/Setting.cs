namespace DigitalBrain;

/// <summary>An options type a module binds from a configuration section.</summary>
/// <param name="OptionsType">The options type.</param>
/// <param name="Section">The configuration section.</param>
public sealed record Setting(Type OptionsType, string Section)
{
    private const string Suffix = "Options";

    /// <summary>The options type; the section defaults to its name without an <c>Options</c> suffix.</summary>
    public static Setting Of<TOptions>(string? section = null) where TOptions : class
    {
        var name = typeof(TOptions).Name;
        var fallback = name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
        return new Setting(typeof(TOptions), string.IsNullOrWhiteSpace(section) ? fallback : section);
    }
}
