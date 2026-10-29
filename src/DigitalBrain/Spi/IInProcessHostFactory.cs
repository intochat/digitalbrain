namespace DigitalBrain.Spi;

/// <summary>Starts a whole brain host inside the test process. A runtime package implements it.</summary>
public interface IInProcessHostFactory
{
    /// <summary>Starts a host.</summary>
    Task<IInProcessHost> Start(InProcessHostOptions options, CancellationToken ct);
}

/// <summary>A running in-process host.</summary>
public interface IInProcessHost : IAsyncDisposable
{
    /// <summary>The host's services.</summary>
    IServiceProvider Services { get; }

    /// <summary>The home folder that holds the host's data.</summary>
    string Home { get; }

    /// <summary>Stops and starts the host over the same home folder.</summary>
    Task Restart(CancellationToken ct);
}
