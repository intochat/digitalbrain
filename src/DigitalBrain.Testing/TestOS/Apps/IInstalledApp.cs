namespace DigitalBrain.Testing;

/// <summary>
/// An installed app seen without its class type: what <see cref="AppBaseline"/> drives, so an app compiled at run time (a script app)
/// is tested the same way as a compiled one. <see cref="InstalledApp{TApp}"/> implements it.
/// </summary>
public interface IInstalledApp
{
    /// <summary>The manifest the app was installed with.</summary>
    AppManifest Manifest { get; }

    /// <summary>The operations the app offers.</summary>
    IReadOnlyList<string> Operations { get; }

    /// <summary>The app instance, for its recorded error state.</summary>
    App Instance { get; }

    /// <summary>The latest presentation the app published.</summary>
    AppPresentation Presentation { get; }

    /// <summary>The error the app's <c>Side</c> or <c>Main</c> property threw when last read, or null.</summary>
    Exception? PresentationError { get; }

    /// <summary>Runs <paramref name="operation"/> as a turn; a null <paramref name="argument"/> is the empty input.</summary>
    Task Request(string operation, object? argument = null);
}
