using System.Reflection;

namespace DigitalBrain.Apps.Hosting;

/// <summary>Serializes and runs turns of a hosted <see cref="DigitalBrain.App"/> instance.</summary>
public sealed class AppTurns
{
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private readonly Func<CancellationToken, Task>? _afterTurn;
    private Exception? _presentationError;

    public AppTurns(App app, AppDefinition definition, Func<CancellationToken, Task>? afterTurn = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(definition);
        App = app;
        Definition = definition;
        _afterTurn = afterTurn;
    }

    public App App { get; }

    public AppDefinition Definition { get; }

    public Exception? PresentationError
    {
        get
        {
            lock (_gate) { return _presentationError; }
        }
        internal set
        {
            lock (_gate) { _presentationError = value; }
        }
    }

    public Task Request(string operation, object? argument, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (Definition.FindOperation(operation) is null)
        {
            var ops = string.Join(", ", Definition.Operations.Values.Select(m => m.Name).Order(StringComparer.Ordinal));
            throw new ArgumentException($"App '{Definition.Id}' has no operation '{operation}'. Operations: {ops}.", nameof(operation));
        }
        return Enqueue(token => Definition.Invoke(App, operation, argument, json: null, token), ct);
    }

    public Task Enqueue(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            var turn = RunAfter(_tail, work, ct);
            _tail = turn.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return turn;
        }
    }

    private async Task RunAfter(Task previous, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        await previous.ConfigureAwait(false);
        await Task.Yield();
        try
        {
            await work(ct).ConfigureAwait(false);
        }
        finally
        {
            if (_afterTurn is not null)
            {
                await _afterTurn(ct).ConfigureAwait(false);
            }
        }
    }
}
