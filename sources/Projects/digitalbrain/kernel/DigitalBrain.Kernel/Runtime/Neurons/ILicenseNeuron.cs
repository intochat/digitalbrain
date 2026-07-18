using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// Interface for the LicenseNeuron which issues and verifies signed license grants.
/// </summary>
public interface ILicenseNeuron : INeuronWithStringKey
{
    Task CheckLicenseAgreementAsync();
    Task<string> IssueLicenseAsync(string bundleId, string userId);
    Task<bool> VerifyLicenseAsync(string licenseToken, string bundleId, string userId);
}
