using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace DigitalBrain.Mcp;

public static class McpAuthorizationElicitation
{
    public static UrlElicitationRequiredException For(AuthorizationRequired requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        return new UrlElicitationRequiredException(
            $"{requirement.ServerDisplayName} requires sign-in before the operation can continue.",
            [
                new ElicitRequestParams
                {
                    Mode = "url",
                    ElicitationId = requirement.State,
                    Url = requirement.SignInUrl.AbsoluteUri,
                    Message =
                        $"{requirement.ServerDisplayName} requires sign-in before the operation can continue.",
                },
            ]);
    }
}
