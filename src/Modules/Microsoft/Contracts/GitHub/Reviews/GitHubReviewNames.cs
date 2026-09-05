using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Microsoft.GitHub;

public static class GitHubReviewNames
{
    public static string InstanceName(PrincipalId principal, string bindingId, string behaviorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorName);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bindingId + "\n" + behaviorName)));
        return PrincipalPartition.InstanceName(principal, "github-review-" + hash[..32]);
    }
}
