using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Apps;

// The apps a person is drafting, newest activity first. Keyed by owner; every draft change
// re-registers its entry here, so reopening the screen finds work in progress instead of
// stranding it behind a fresh draft id.
[Alias("intochat.app-drafts"), Orleans.Metadata.DefaultGrainType("intochat.app-drafts")]
public interface IAppDrafts : INeuron
{
    Task Record(string draftId, string title, AppDraftStatus status);
    [ReadOnly, AlwaysInterleave] Task<IReadOnlyList<AppDraftEntry>> List();
}

