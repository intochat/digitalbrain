using TripRadar.DeploymentKit.Constants;

namespace TripRadar.DeploymentKit.Utilities;

/// <summary>
/// Helper class for generating consistent resource tags across all Azure resources
/// </summary>
public static class ResourceTagHelper
{
    /// <summary>
    /// Gets standard tags that should be applied to all resources
    /// </summary>
    /// <param name="environment">Environment name (dev, staging, prod)</param>
    /// <param name="resourceType">Type of the resource for categorization</param>
    /// <param name="additionalTags">Additional custom tags</param>
    /// <returns>Dictionary of tags</returns>
    public static InputMap<string> GetStandardTags(string environment, string resourceType, Dictionary<string, string>? additionalTags = null)
    {
        var tags = new Dictionary<string, string>
        {
            ["Environment"] = environment,
            ["Project"] = InfrastructureConstants.Tags.Project,
            ["ResourceType"] = resourceType,
            ["ManagedBy"] = InfrastructureConstants.Tags.ManagedBy,
            ["CreatedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Owner"] = InfrastructureConstants.Tags.Owner
        };

        // Add any additional tags
        if (additionalTags != null)
        {
            foreach (var tag in additionalTags)
            {
                tags[tag.Key] = tag.Value;
            }
        }

        var inputMap = new InputMap<string>();
        foreach (var tag in tags)
        {
            inputMap[tag.Key] = tag.Value;
        }
        return inputMap;
    }
}

