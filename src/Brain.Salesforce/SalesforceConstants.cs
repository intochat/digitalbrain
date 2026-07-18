using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Salesforce;

public static class SalesforceConstants
{
    public const string FeedStreamNamespace = "salesforce.feed";
    public const string EffectDoneFlagPrefix = "effect-done:";
    public const string SurfaceTextFlag = "surface-text";
    public const string AutoDrainFlag = "auto-drain";
    public const string LifecycleJournalResult = "journal-result";
    public const string LifecyclePublishOutcome = "publish-outcome";

    public static Guid FeedStreamIdFor(string grainKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(grainKey));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static Guid OutcomeEventId(Guid effectId, string kind)
    {
        var bytes = effectId.ToByteArray();
        var tag = (byte)(kind.GetHashCode(StringComparison.Ordinal) & 0xFF);
        bytes[15] ^= tag;
        return new Guid(bytes);
    }
}
