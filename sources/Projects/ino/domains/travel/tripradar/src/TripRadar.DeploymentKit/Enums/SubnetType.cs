using System.ComponentModel;

namespace TripRadar.DeploymentKit.Enums;

/// <summary>
/// Represents the types of subnet configurations.
/// </summary>
public enum SubnetType
{
    /// <summary>
    /// Subnet for container applications.
    /// </summary>
    [Description("containerapp")]
    ContainerApp,

    /// <summary>
    /// Subnet for database resources.
    /// </summary>
    [Description("database")]
    Database,

    /// <summary>
    /// Subnet for Application Gateway.
    /// </summary>
    [Description("ApplicationGateway")]
    ApplicationGateway
}

