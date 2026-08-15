using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;

namespace DigitalBrain.ProductHost.Protocol;

public interface IProductRuntimeClient
{
    Task<IReadOnlyList<BrainModuleDescriptor>> GetModulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BrainOperationDescriptor>> GetOperationsAsync(CancellationToken cancellationToken);

    Task<BrainActivityReceipt> InvokeAsync(
        BrainOperationInvocation invocation,
        CancellationToken cancellationToken);

    Task<BrainActivitySnapshot?> GetActivityAsync(
        Guid activity,
        string workspace,
        CancellationToken cancellationToken);

    Task<BrainJournalPage> GetJournalAsync(
        Guid activity,
        string workspace,
        long afterSequence,
        int take,
        CancellationToken cancellationToken);

    Task<BrainSnapshot> GetBrainAsync(
        string workspace,
        CancellationToken cancellationToken);
}
