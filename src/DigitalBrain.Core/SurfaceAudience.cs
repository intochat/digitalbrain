using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public enum SurfaceAudienceKind
{
    Principal,
    Workspace,
    Public
}

public sealed record SurfaceAudience(SurfaceAudienceKind Kind, string Id);

public static class PrincipalScope
{
    public static string Id(PrincipalRef principal)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = (int)principal.Kind,
            value = principal.Value
        });
        return $"p{(int)principal.Kind}-{Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant()}";
    }
}
