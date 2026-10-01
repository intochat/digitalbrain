using DigitalBrain.Apps;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Apps;

public static class InstalledPackages
{
    public static string AppKey(PackageId id) => BrainScope.CurrentId() + "/packages/" + id;
}

