namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceAuthenticationRequiredException() : HttpRequestException(
    "Salesforce authorization is required. Use the Salesforce login action to continue.",
    inner: null,
    System.Net.HttpStatusCode.Unauthorized);
