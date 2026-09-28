namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingNames
{
    public const string Kernel = "Kernel";
    public const string Orleans = "digitalbrain";

    public static string ForModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        var name = moduleType.Name;
        return name.EndsWith("Module", StringComparison.Ordinal) ? name[..^"Module".Length] : name;
    }
}