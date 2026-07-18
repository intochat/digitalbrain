using System.ComponentModel;

namespace TripRadar.DeploymentKit.Enums;

/// <summary>
/// Azure Key Vault SKU types
/// </summary>
public enum KeyVaultSkuType
{
    /// <summary>
    /// Standard SKU for Key Vault
    /// </summary>
    [Description("standard")]
    Standard,

    /// <summary>
    /// Premium SKU for Key Vault with HSM support
    /// </summary>
    [Description("premium")]
    Premium
}
