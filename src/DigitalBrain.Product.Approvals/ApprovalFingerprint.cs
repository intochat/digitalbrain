using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Product.Approvals;

internal static class ApprovalFingerprint
{
    internal static string Compute(
        string proposalId,
        string title,
        string summary,
        IReadOnlyList<ApprovalEvidence> evidence,
        IReadOnlyList<ApprovalChange> changes,
        ApprovalActionBinding action,
        DateTimeOffset expiresAt,
        ApprovalReviewContext? reviewContext)
    {
        var canonical = new StringBuilder();
        Append(canonical, proposalId);
        Append(canonical, title);
        Append(canonical, summary);
        Append(canonical, expiresAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture));
        Append(canonical, action.ActionKind);
        Append(canonical, action.ActionId);
        Append(canonical, action.ActionFingerprint);
        Append(canonical, action.ExecutionTarget.Kind);
        Append(canonical, action.ExecutionTarget.Name);
        Append(canonical, reviewContext is null
            ? null
            : ((int)reviewContext.Kind).ToString(CultureInfo.InvariantCulture));
        Append(canonical, reviewContext?.OpaqueContextRef);
        Append(canonical, evidence.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var item in evidence)
        {
            Append(canonical, item.Source);
            Append(canonical, item.Summary);
            Append(canonical, item.ReferenceUri?.OriginalString);
        }

        Append(canonical, changes.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var change in changes)
        {
            Append(canonical, change.Field);
            Append(canonical, change.Before);
            Append(canonical, change.ProposedValue);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Append(StringBuilder canonical, string? value)
    {
        var length = value?.Length ?? -1;
        canonical.Append(length.ToString(CultureInfo.InvariantCulture));
        canonical.Append(':');
        canonical.Append(value);
        canonical.Append('|');
    }
}
