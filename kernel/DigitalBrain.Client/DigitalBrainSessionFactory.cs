using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain;

public sealed class DigitalBrainSessionFactory(
    IServiceScopeFactory scopeFactory,
    BrainOwnerContext ownerContext)
{
    public DigitalBrainSession Create(BrainOwnerId owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Value, nameof(owner));
        if (ownerContext.Current is not null)
            throw new InvalidOperationException(
                "An owner session is already active in this execution context.");

        var scope = scopeFactory.CreateAsyncScope();
        ownerContext.Current = owner;
        try
        {
            return new DigitalBrainSession(
                scope,
                ownerContext,
                owner,
                scope.ServiceProvider.GetRequiredService<DigitalBrainClient>());
        }
        catch
        {
            ownerContext.Current = null;
            scope.Dispose();
            throw;
        }
    }
}

public sealed class DigitalBrainSession : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly BrainOwnerContext _ownerContext;
    private readonly BrainOwnerId _owner;
    private int _disposed;

    internal DigitalBrainSession(
        AsyncServiceScope scope,
        BrainOwnerContext ownerContext,
        BrainOwnerId owner,
        DigitalBrainClient client)
    {
        _scope = scope;
        _ownerContext = ownerContext;
        _owner = owner;
        Client = client;
    }

    public DigitalBrainClient Client { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;
        if (_ownerContext.Current == _owner)
            _ownerContext.Current = null;
        return _scope.DisposeAsync();
    }
}
