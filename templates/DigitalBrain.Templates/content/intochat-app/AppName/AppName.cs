using DigitalBrain;
using DigitalBrain.UI;

namespace APP_NAMESPACE;

/// <summary>Asks the app to show its start view.</summary>
[Signal("APP_ID.opened")]
public sealed record Opened : Signal;

/// <summary>The turn state of <see cref="APP_CLASS"/>.</summary>
public sealed record APP_CLASSState(string Message)
{
    public APP_CLASSState() : this("Welcome to APP_CLASS.") { }
}

/// <summary>
/// The APP_CLASS app. What it does is written in the spec comment next to this file; every operation described there is one
/// <c>[On]</c> method here. The app id is frozen once people install it.
/// </summary>
[App("APP_ID")]
public sealed class APP_CLASS : App<APP_CLASSState>
{
    /// <summary>Shows the start view.</summary>
    [On]
    public void Open() => Set(new("Welcome to APP_CLASS."));

    /// <summary>Renders the main view from <see cref="App{TState}.State"/>.</summary>
    public View Render() => new("APP_CLASS", new Column([new Text(State.Message)]));
}
