using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Salesforce;

public static class SalesforceConstants
{
    public const string FeedStreamNamespace = "salesforce.feed";
    public const string EffectDoneFlagPrefix = "effect-done:";
    public const string EffectFailedFlagPrefix = "effect-failed:";
    public const string SurfaceTextFlag = "surface-text";
    public const string AutoDrainFlag = "auto-drain";
    public const string FailNextOutcomePublishFlag = "fail-next-outcome-publish";

    public static Guid FeedStreamIdFor(string grainKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(grainKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static Guid OutcomeEventId(Guid effectId, string kind)
    {
        var kindBytes = Encoding.UTF8.GetBytes(kind);
        var input = new byte[16 + kindBytes.Length];
        effectId.TryWriteBytes(input.AsSpan(0, 16));
        kindBytes.CopyTo(input.AsSpan(16));
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }
}
