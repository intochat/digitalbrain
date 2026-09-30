namespace DigitalBrain.Aspire.Hosting;

public static class DigitalBrainHostingNames
{
    public const string Kernel = "Kernel";
    public const string Orleans = "digitalbrain";

    // Azurite lifetime. Product runs persist; a test host passes false so deployments do not share a volume.
    public const string PersistentStorageKey = "DigitalBrain:Hosting:PersistentStorage";

    public static string ForModule(Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        var name = moduleType.Name;
        return name.EndsWith("Module", StringComparison.Ordinal) ? name[..^"Module".Length] : name;
    }
}
