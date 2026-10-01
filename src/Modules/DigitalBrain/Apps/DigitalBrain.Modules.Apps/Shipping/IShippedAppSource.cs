namespace DigitalBrain.Apps;

public sealed record ShippedApp(PackageId Package, PackageContent Content);

public interface IShippedAppSource
{
    string Publisher { get; }
    IReadOnlyList<ShippedApp> Load();
}
