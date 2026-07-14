using System.Net;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;

namespace DigitalBrain.Mcp;

public sealed record AuthorizationFlowProxyOptions(Uri InternalOrigin)
{
    public static AuthorizationFlowProxyOptions FromConfiguration(IConfiguration configuration, RuntimeProfile profile)
    {
        var configured = configuration["DigitalBrain:Runtime:OAuth:InternalOrigin"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin) || origin.Host.Length == 0 || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0 ||
            origin.AbsolutePath is not ("" or "/") ||
            profile == RuntimeProfile.Production && origin.Scheme != Uri.UriSchemeHttps ||
            profile != RuntimeProfile.Production && origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("DigitalBrain:Runtime:OAuth:InternalOrigin must be a trusted runtime origin.");
        return new(origin);
    }
}

public sealed class AuthorizationFlowStartProxy(HttpClient client, AuthorizationFlowProxyOptions options)
{
    public async Task<IResult> StartAsync(string provider, HttpRequest request, CancellationToken cancellationToken)
    {
        SetBrowserResponseHeaders(request.HttpContext.Response);
        var target = (request.Path.Value ?? string.Empty) + (request.QueryString.Value ?? string.Empty);
        if (!OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out var flowReference))
            return Results.BadRequest();

        var internalTarget = new Uri(options.InternalOrigin, $"/oauth/start/{provider}?f={Uri.EscapeDataString(flowReference)}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(internalTarget, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        using var received = response;
        if (received.StatusCode is not (
                HttpStatusCode.MovedPermanently or
                HttpStatusCode.Found or
                HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect) ||
            received.Headers.Location is not { IsAbsoluteUri: true } location ||
            location.OriginalString.Length > 4096 ||
            !OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl(provider, location.AbsoluteUri))
            return Results.BadRequest();
        return Results.Redirect(location.AbsoluteUri, permanent: false, preserveMethod: false);
    }

    private static void SetBrowserResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        response.Headers.XContentTypeOptions = "nosniff";
    }
}
