namespace DigitalBrain.Runtime.Orleans;

/// <summary>One process a topology runs.</summary>
/// <param name="Name">The process name.</param>
/// <param name="Image">The container image.</param>
/// <param name="Ports">The ports it listens on.</param>
/// <param name="Volumes">The volumes it mounts.</param>
/// <param name="EnvironmentKeys">The environment variable names it reads.</param>
public sealed record ServiceProcess(string Name, string Image, IReadOnlyList<int> Ports, IReadOnlyList<string> Volumes, IReadOnlyList<string> EnvironmentKeys);

/// <summary>The process shape of a topology. Aspire, the CLI and the installer render it; none keeps its own description.</summary>
/// <param name="Processes">The processes.</param>
/// <param name="AzureResourceKinds">The Azure resource kinds the topology needs (empty off cloud).</param>
public sealed record Services(IReadOnlyList<ServiceProcess> Processes, IReadOnlyList<string> AzureResourceKinds)
{
    /// <summary>No processes and no resources.</summary>
    public static Services Empty { get; } = new([], []);
}
