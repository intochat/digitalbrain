namespace DigitalBrain.Runtime.Runtime;

/// <summary>
/// Defines the contract for unpacking, verifying, compiling, and registering packaged .bdom domains.
/// </summary>
public interface IBundleInstaller
{
    /// <summary>
    /// Unpacks, verifies, compiles, and registers a local .bdom bundle.
    /// </summary>
    /// <param name="bundleFilePath">The absolute path to the local .bdom file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating the success or failure of the installation.</returns>
    Task<BundleInstallResult> InstallLocalAsync(string bundleFilePath, CancellationToken cancellationToken);
}

/// <summary>
/// Represents the result of a bundle installation operation.
/// </summary>
/// <param name="Success">Indicates if the installation succeeded.</param>
/// <param name="BundleId">The unique ID of the bundle.</param>
/// <param name="RegisteredNeuronFqns">List of fully qualified neuron names registered by this bundle.</param>
/// <param name="Diagnostics">Any warning, error, or status logs generated during installation.</param>
public sealed record BundleInstallResult(
    bool Success,
    string BundleId,
    string[] RegisteredNeuronFqns,
    string[] Diagnostics);
