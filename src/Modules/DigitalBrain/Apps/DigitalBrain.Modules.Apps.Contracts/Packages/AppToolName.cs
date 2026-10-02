using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Apps;

public static class AppToolName
{
    public static string For(PackageId id, string operation)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id + "/" + operation)))[..16];
        var name = id.Name.Replace('-', '_');
        var action = operation.Replace('-', '_');
        return $"app_{name[..Math.Min(name.Length, 24)]}_{action[..Math.Min(action.Length, 16)]}_{hash}";
    }
}
