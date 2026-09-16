namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingNames
{
    public const string Core = "Core";
    public const string Orleans = "digitalbrain";

    public static string ForModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (moduleType.Name == "UIModule")
        {
            return "Flutter";
        }

        var name = moduleType.Name;
        return name.EndsWith("Module", StringComparison.Ordinal) ? name[..^"Module".Length] : name;
    }
}
