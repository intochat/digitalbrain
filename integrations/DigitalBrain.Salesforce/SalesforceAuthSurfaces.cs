using DigitalBrain.Core;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Salesforce;

public static class SalesforceAuthSurfaces
{
    public static object CredentialForm(string emitter, string? clientId = null, string? message = null)
    {
        // Stubbed to allow build during system redesign (avoids sub-namespace 'Ui'/'Pack'/'Distribution' resolution issues in integration assemblies).
        // Original logic using ConfigFormSurface and widget trees can be restored after further extraction of cross-cutting types.
        return null;
    }
}
