using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace IntoChat.Marketplace;

// The apps a person is drafting, newest activity first. Keyed by owner; every draft change
// re-registers its entry here, so reopening the screen finds work in progress instead of
// stranding it behind a fresh draft id.
[Alias("intochat.app-drafts"), Orleans.Metadata.DefaultGrainType("intochat.app-drafts")]
public interface IAppDrafts : INeuron
{
    Task Record(string draftId, string title, AppDraftStatus status);
    [ReadOnly, AlwaysInterleave] Task<IReadOnlyList<AppDraftEntry>> List();
}

[GenerateSerializer, Alias("intochat.app-draft-entry")]
public sealed record AppDraftEntry(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] AppDraftStatus Status,
    [property: Id(3)] DateTimeOffset UpdatedAt);

[GenerateSerializer, Alias("intochat.app-drafts-state")]
public sealed record AppDraftsState
{
    [Id(0)] public AppDraftEntry[] Entries { get; init; } = [];
}

[GenerateSerializer, Alias("intochat.app-drafts-changed")]
public sealed record AppDraftsChanged([property: Id(0)] string Owner) : Signal;

[GrainType("intochat.app-drafts")]
internal sealed class AppDraftsNeuron(
    [PersistentState("intochat.app-drafts", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppDraftsState> store,
    TimeProvider clock)
    : Neuron<AppDraftsState>(store), IAppDrafts
{
    private const int MaxEntries = 100;

    public Task Record(string draftId, string title, AppDraftStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var entry = new AppDraftEntry(draftId, title, status, clock.GetUtcNow());
        AppDraftEntry[] entries = [entry, .. Snapshot.Entries.Where(existing => existing.Id != draftId).Take(MaxEntries - 1)];
        return Save(Snapshot with { Entries = entries }, new AppDraftsChanged(this.GetPrimaryKeyString()));
    }

    public Task<IReadOnlyList<AppDraftEntry>> List() => Task.FromResult<IReadOnlyList<AppDraftEntry>>(Snapshot.Entries);
}
