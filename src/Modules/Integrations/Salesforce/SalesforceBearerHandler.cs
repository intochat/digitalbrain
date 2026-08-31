using System.Net;
using System.Net.Http.Headers;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceBearerHandler(SalesforceConnections connections, OwnerId owner)
    : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Mcp.McpIntegrationEndpoint.ValidateSalesforceUri(request.RequestUri!);
        var token = await connections.GetAccessTokenAsync(owner, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await connections.RejectTokenAsync(owner, token, cancellationToken).ConfigureAwait(false);
            throw new SalesforceAuthenticationRequiredException();
        }
        return response;
    }
}
