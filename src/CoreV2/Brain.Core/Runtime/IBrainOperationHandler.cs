using Brain.Abstractions.Runtime;

namespace Brain.Core.Runtime;

public interface IBrainOperationHandler
{
    BrainOperationDescriptor Descriptor { get; }

    Task<string> ExecuteAsync(
        BrainOperationExecutionContext context,
        CancellationToken cancellationToken);
}
