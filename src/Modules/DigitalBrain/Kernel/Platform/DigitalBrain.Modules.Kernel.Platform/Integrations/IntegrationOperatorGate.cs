using DigitalBrain.Contracts.Enforcement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Platform.Integrations;

internal static class IntegrationOperatorGate
{
    // Mirrors Identity's single-operator login; the Sdk cannot reference Identity.
    private const string OpenPostureLogin = "owner";

    // Deployment registrations belong to the operator; a per-account Owner role is not that.
    public static bool Allows(CallerContext? caller, IConfiguration configuration)
    {
        if (caller is null || caller.Kind != CallerKind.User || caller.StampedBy != TrustedEdge.AuthenticatedHttp)
        {
            return false;
        }

        var username = configuration["DigitalBrain:Auth:Username"];
        var hasCredential = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(configuration["DigitalBrain:Auth:Password"]);
        return string.Equals(caller.PrincipalId, hasCredential ? username : OpenPostureLogin, StringComparison.Ordinal);
    }
}
