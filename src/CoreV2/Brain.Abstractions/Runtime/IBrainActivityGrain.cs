using Brain.Abstractions.Journal;

namespace Brain.Abstractions.Runtime;

public interface IBrainActivityGrain : IGrainWithStringKey
{
    Task<BrainActivityReceipt> StartAsync(Guid activityId, BrainOperationInvocation invocation);

    Task<BrainJournalRecord> AppendAsync(BrainJournalWrite write);

    Task<BrainJournalPage> ReadJournalAsync(string workspaceId, long afterSequence, int take);

    Task<BrainActivitySnapshot?> GetAsync(string workspaceId);
}
