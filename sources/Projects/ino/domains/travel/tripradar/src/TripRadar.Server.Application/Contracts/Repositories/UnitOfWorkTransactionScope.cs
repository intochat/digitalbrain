using System.Transactions;

namespace TripRadar.Server.Application.Contracts.Repositories;

public sealed class UnitOfWorkTransactionScope(Func<CancellationToken, Task> commitAsync, Func<ValueTask> disposeAsync) : IAsyncDisposable
{
    public UnitOfWorkTransactionScope(TransactionScope transactionScope) : this(_ =>
    {
        transactionScope.Complete();
        return Task.CompletedTask;
    }, () =>
    {
        transactionScope.Dispose();
        return ValueTask.CompletedTask;
    })
    {
    }

    public static UnitOfWorkTransactionScope Noop() => new(_ => Task.CompletedTask, () => ValueTask.CompletedTask);

    public Task CommitAsync(CancellationToken cancellationToken = default) => commitAsync(cancellationToken);

    public ValueTask DisposeAsync() => disposeAsync();
}
