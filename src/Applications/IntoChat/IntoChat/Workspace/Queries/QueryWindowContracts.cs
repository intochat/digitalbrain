using Orleans;
using Orleans.Metadata;

namespace IntoChat.Workspace.Queries;

[GenerateSerializer, Alias("intochat.query-window-result")]
public sealed record QueryWindowResult(
    [property: Id(0)] string WindowId,
    [property: Id(1)] string TableId,
    [property: Id(2)] string Title,
    [property: Id(3)] long WorkspaceRevision)
{
    public bool RemoteManaged => true;
}

// A short durable journal; the cancellable database work runs outside its activation.
[Alias("intochat.query-window-operation"), DefaultGrainType("intochat.query-window-operation")]
public interface IQueryWindowOperation : IGrainWithStringKey
{
    Task<QueryWindowProgress> Begin(string fingerprint);
    Task TableCreated();
    Task Complete(QueryWindowResult result);
    Task Interrupt();
}

[GenerateSerializer, Alias("intochat.query-window-progress")]
public sealed record QueryWindowProgress
{
    [Id(0)] public string? Fingerprint { get; init; }
    [Id(1)] public string Stage { get; init; } = "pending";
    [Id(2)] public QueryWindowResult? Result { get; init; }
}