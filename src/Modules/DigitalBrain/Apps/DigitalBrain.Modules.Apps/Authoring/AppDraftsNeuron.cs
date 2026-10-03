using DigitalBrain;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("intochat.app-drafts")]
internal sealed class AppDraftsNeuron(
    [PersistentState("intochat.app-drafts", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppDraftsState> store,
    TimeProvider clock)
    : Neuron<AppDraftsState>(store), IAppDrafts
{
    private const int MaxEntries = 100;
    public override DigitalBrain.Kernel.Enforcement.NeuronAccess Access(string operation)
    {
        if (DigitalBrain.Kernel.Enforcement.CallerContextStamper.Require().PrincipalId != this.GetPrimaryKeyString())
        { throw new UnauthorizedAccessException("This draft index belongs to another person."); }
        return DigitalBrain.Kernel.Enforcement.NeuronAccess.PublicOperation;
    }

    public Task Record(string draftId, string title, AppDraftStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var entry = new AppDraftEntry(draftId, title, status, clock.GetUtcNow());
        AppDraftEntry[] entries = [entry, .. Snapshot.Entries.Where(existing => existing.Id != draftId).Take(MaxEntries - 1)];
        return Save(Snapshot with { Entries = entries }, new AppDraftsChanged(this.GetPrimaryKeyString()));
    }

    public Task<IReadOnlyList<AppDraftEntry>> List() => Task.FromResult<IReadOnlyList<AppDraftEntry>>(Snapshot.Entries);
}
