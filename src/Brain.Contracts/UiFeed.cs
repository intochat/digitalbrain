using System.Security.Cryptography;
using System.Text;

namespace Brain.Contracts;

public static class UiFeedFrameTypes
{
    public const string Snapshot = "snapshot";
    public const string Patch = "patch";
    public const string Failure = "failure";
}

[GenerateSerializer, Alias("brain.ui-feed-candidate.v1")]
public sealed record UiFeedCandidate(
    [property: Id(0)] string Type,
    [property: Id(1)] UiSurfaceSnapshot? Snapshot,
    [property: Id(2)] UiSurfacePatch? Patch,
    [property: Id(3)] string? FailureCode)
{
    public static UiFeedCandidate CreateSnapshot(UiSurfaceSnapshot snapshot) =>
        new(UiFeedFrameTypes.Snapshot, snapshot, null, null);

    public static UiFeedCandidate CreatePatch(UiSurfacePatch patch) =>
        new(UiFeedFrameTypes.Patch, null, patch, null);

    public static UiFeedCandidate CreateFailure(string failureCode) =>
        new(UiFeedFrameTypes.Failure, null, null, failureCode);
}

[GenerateSerializer, Alias("brain.ui-feed-frame.v1")]
public sealed record UiFeedFrame(
    [property: Id(0)] int SchemaVersion,
    [property: Id(1)] long Sequence,
    [property: Id(2)] Guid EventId,
    [property: Id(3)] string Type,
    [property: Id(4)] UiSurfaceSnapshot? Snapshot,
    [property: Id(5)] UiSurfacePatch? Patch,
    [property: Id(6)] string? FailureCode)
{
    public const int CurrentSchemaVersion = 1;
}

[GenerateSerializer, Alias("brain.ui-feed-page.v1")]
public sealed record UiFeedPage(
    [property: Id(0)] IReadOnlyList<UiFeedFrame> Frames,
    [property: Id(1)] long NextCursor);

public static class UiFeedStreams
{
    public const string ContractId = "ui.feed.v1";
    public const string InstanceId = "main";
    public const string CandidateNamespace = "ui.feed.candidates";
    public const string LiveNamespace = "ui.feed.live";

    public static string FeedKey(OrganizationId organization, SpaceId space) =>
        new NeuronAddress(organization, space, ContractId, InstanceId).ToGrainKey();

    public static Guid StreamId(OrganizationId organization, SpaceId space)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{organization.Value}\n{space.Value}\n{ContractId}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

[Alias("brain.ui-feed.IUiFeed")]
[NeuronContract(UiFeedStreams.ContractId)]
public interface IUiFeed : IGrainWithStringKey
{
    [Alias("EnsureSubscribedAsync")]
    Task EnsureSubscribedAsync();

    [Alias("ReadAsync")]
    Task<UiFeedPage> ReadAsync(long cursor, int max);
}
