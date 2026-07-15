using System.Text;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureApprovalLedger
{
    private const int ObjectHeaderBytes = 8;
    private const int FieldTagBytes = 2;
    private const int CollectionLengthBytes = 4;
    private const int StringLengthBytes = 4;
    private const int NullableMarkerBytes = 1;
    private const int EnumBytes = 4;
    private const int Int32Bytes = 4;
    private const int Int64Bytes = 8;
    private const int DateTimeOffsetBytes = 16;

    internal static int CheckedAdd(int left, int right) => checked(left + right);

    public static FeatureApprovalState[] Compact(FeatureApprovalState[] approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        if (approvals.Length == 0)
            return [];

        var normalized = approvals.Select(CompactHistory).ToArray();
        var current = normalized
            .Where(approval => approval.Status != FeatureApprovalStatus.Superseded)
            .ToArray();
        var retainedCount = current.Length;
        var retainedBytes = SerializedBytes(current);
        if (retainedCount >= FeatureLimits.ApprovalLedgerRecords ||
            retainedBytes >= FeatureLimits.ApprovalLedgerUtf8Bytes)
            return current;

        var retainedHistory = new HashSet<int>();
        var history = normalized
            .Select((approval, index) => (Approval: approval, Index: index))
            .Where(candidate => candidate.Approval.Status == FeatureApprovalStatus.Superseded)
            .OrderByDescending(candidate => candidate.Approval.Revision)
            .ThenBy(candidate => candidate.Approval.ApprovalId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Index);
        foreach (var candidate in history)
        {
            if (retainedCount >= FeatureLimits.ApprovalLedgerRecords)
                break;
            var nextBytes = CheckedAdd(retainedBytes, SerializedRecordBytes(candidate.Approval));
            if (nextBytes > FeatureLimits.ApprovalLedgerUtf8Bytes)
                break;
            retainedHistory.Add(candidate.Index);
            retainedCount++;
            retainedBytes = nextBytes;
        }

        return normalized
            .Where((approval, index) =>
                approval.Status != FeatureApprovalStatus.Superseded || retainedHistory.Contains(index))
            .ToArray();
    }

    public static int SerializedBytes(IEnumerable<FeatureApprovalState> approvals)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        var bytes = CollectionLengthBytes;
        foreach (var approval in approvals)
        {
            ArgumentNullException.ThrowIfNull(approval);
            bytes = CheckedAdd(bytes, SerializedRecordBytes(approval));
        }
        return bytes;
    }

    private static FeatureApprovalState CompactHistory(FeatureApprovalState approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        return approval.Status == FeatureApprovalStatus.Superseded && approval.Release.Source is not null
            ? approval with { Release = approval.Release with { Source = null } }
            : approval;
    }

    private static int SerializedRecordBytes(FeatureApprovalState approval)
    {
        var bytes = ObjectBytes(11);
        bytes = CheckedAdd(bytes, TextBytes(approval.ApprovalId));
        bytes = CheckedAdd(bytes, ValueObjectBytes(approval.InstallationId.Value));
        bytes = CheckedAdd(bytes, ReleaseBytes(approval.Release));
        bytes = CheckedAdd(bytes, TextArrayBytes(approval.AddedCapabilities));
        bytes = CheckedAdd(bytes, TextArrayBytes(approval.RemovedCapabilities));
        bytes = CheckedAdd(bytes, EnumBytes);
        bytes = CheckedAdd(bytes, OptionalTextBytes(approval.DecisionId));
        bytes = CheckedAdd(bytes, NullableMarkerBytes + (approval.DecidedAt is null ? 0 : DateTimeOffsetBytes));
        bytes = CheckedAdd(bytes, Int64Bytes);
        bytes = CheckedAdd(bytes, GrantArrayBytes(approval.Grants));
        bytes = CheckedAdd(bytes, OptionalValueObjectBytes(approval.DecisionActorId?.Value));
        return bytes;
    }

    private static int ReleaseBytes(FeatureReleaseMetadata release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var bytes = ObjectBytes(6);
        bytes = CheckedAdd(bytes, ValueObjectBytes(release.Digest.Value));
        bytes = CheckedAdd(bytes, TextBytes(release.SourceReference));
        bytes = CheckedAdd(bytes, EnumBytes);
        bytes = CheckedAdd(bytes, TextArrayBytes(release.RequestedCapabilities));
        bytes = CheckedAdd(bytes, TextArrayBytes(release.Dependencies));
        bytes = CheckedAdd(bytes, NullableMarkerBytes);
        if (release.Source is { } source)
            bytes = CheckedAdd(bytes, SourceBytes(source));
        return bytes;
    }

    private static int SourceBytes(FeatureSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bytes = ObjectBytes(3);
        bytes = CheckedAdd(bytes, TextBytes(source.ImplementationProjectPath));
        bytes = CheckedAdd(bytes, TextBytes(source.ScenarioProjectPath));
        bytes = CheckedAdd(bytes, CollectionLengthBytes);
        ArgumentNullException.ThrowIfNull(source.Files);
        foreach (var file in source.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            var fileBytes = ObjectBytes(2);
            fileBytes = CheckedAdd(fileBytes, TextBytes(file.Path));
            fileBytes = CheckedAdd(fileBytes, TextBytes(file.Content));
            bytes = CheckedAdd(bytes, fileBytes);
        }
        return bytes;
    }

    private static int GrantArrayBytes(FeatureGrantState[] grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        var bytes = CollectionLengthBytes;
        foreach (var grant in grants)
        {
            ArgumentNullException.ThrowIfNull(grant);
            var grantBytes = ObjectBytes(5);
            grantBytes = CheckedAdd(grantBytes, TextBytes(grant.CapabilityId));
            grantBytes = CheckedAdd(grantBytes, Int32Bytes);
            grantBytes = CheckedAdd(grantBytes, OptionalValueObjectBytes(grant.ProviderConnectionId?.Value));
            grantBytes = CheckedAdd(grantBytes, TextBytes(grant.ConstraintsJson));
            grantBytes = CheckedAdd(grantBytes, OptionalTextBytes(grant.Provider));
            bytes = CheckedAdd(bytes, grantBytes);
        }
        return bytes;
    }

    private static int TextArrayBytes(string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var bytes = CollectionLengthBytes;
        foreach (var value in values)
            bytes = CheckedAdd(bytes, TextBytes(value));
        return bytes;
    }

    private static int ValueObjectBytes(string value) => CheckedAdd(ObjectBytes(1), TextBytes(value));

    private static int OptionalValueObjectBytes(string? value) => value is null
        ? NullableMarkerBytes
        : CheckedAdd(NullableMarkerBytes, ValueObjectBytes(value));

    private static int OptionalTextBytes(string? value) => value is null
        ? NullableMarkerBytes
        : CheckedAdd(NullableMarkerBytes, TextBytes(value));

    private static int TextBytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return CheckedAdd(StringLengthBytes, Encoding.UTF8.GetByteCount(value));
    }

    private static int ObjectBytes(int fieldCount) =>
        CheckedAdd(ObjectHeaderBytes, checked(fieldCount * FieldTagBytes));
}
