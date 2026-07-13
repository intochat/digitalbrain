using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Core.Runtime;

public enum SurfaceAudienceKind
{
    Actor,
    Owner,
    Public
}

public sealed record SurfaceAudience(SurfaceAudienceKind Kind, string Id);

public static class ActorScope
{
    public static string Id(ActorId actorId)
    {
        var canonical = Encoding.UTF8.GetBytes(actorId.Value);
        return $"a-{Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant()}";
    }
}
