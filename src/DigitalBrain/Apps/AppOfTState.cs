namespace DigitalBrain;

/// <summary>
/// A stateful app whose <c>[On]</c> operations update <see cref="State"/> through <see cref="Set(TState)"/>
/// and present views declaratively via <c>Render()</c> / <c>Main</c> and <c>RenderSide()</c> / <c>Side</c>.
/// </summary>
/// <typeparam name="TState">The app's turn state, initialized with <c>new()</c>.</typeparam>
public abstract class App<TState> : App
    where TState : new()
{
    private readonly Lock _stateGate = new();
    private TState _state = new();

    /// <summary>Creates the app with its initial state.</summary>
    protected App()
    {
    }

    /// <summary>The current state of the app.</summary>
    public TState State
    {
        get
        {
            lock (_stateGate) { return _state; }
        }
    }

    /// <summary>Updates <see cref="State"/> to <paramref name="next"/>, clears any recorded error, and returns <paramref name="next"/>.</summary>
    protected TState Set(TState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_stateGate) { _state = next; }
        ClearError();
        return next;
    }
}
