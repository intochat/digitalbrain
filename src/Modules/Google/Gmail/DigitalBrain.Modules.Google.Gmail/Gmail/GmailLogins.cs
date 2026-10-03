using DigitalBrain.Kernel;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google.Gmail;

internal sealed class GmailLogins(GmailRegistration registration, TimeProvider clock)
    : BrowserLogins(LoginDefinition, clock)
{
    internal const string ComposeScope = "compose";

    internal static readonly BrowserLoginDefinition LoginDefinition = new(
        "gmail",
        "Gmail",
        "GmailIntegration",
        "/integrations/gmail/login",
        "/integrations/gmail/callback",
        "Sign in with Google to connect Gmail. Credentials stay outside the conversation. Login never creates a draft.");

    // The synchronous origin the login surface checks requests against; the registration is one cheap read-only grain call.
    protected override Uri? PublicOrigin
    {
        get
        {
            var snapshot = registration.ReadAsync().GetAwaiter().GetResult();
            return GmailRegistration.Explain(snapshot) is null ? GmailRegistration.PublicOriginOf(snapshot) : null;
        }
    }

    public override async Task<object?> DescribeUnavailabilityAsync()
        => GmailRegistration.Explain(await registration.ReadAsync().ConfigureAwait(false));
}
