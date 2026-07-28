using System.Diagnostics;
using System.Net;
using System.Text;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class LocalLoopbackMcpAuthorizationCallback
{
    internal static Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return AuthorizeAsyncCore(
            context.AuthorizationUri,
            context.RedirectUri,
            static startInfo => { Process.Start(startInfo); },
            cancellationToken);
    }

    private static async Task<AuthorizationResult?> AuthorizeAsyncCore(
        Uri authorizationUri,
        Uri redirectUri,
        Action<ProcessStartInfo> startBrowser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentNullException.ThrowIfNull(startBrowser);

        if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException(
                "The local MCP authorization adapter accepts only an HTTP loopback redirect URI.");
        }

        _ = QueryValue(authorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");
        using var listener = new HttpListener();
        listener.Prefixes.Add($"{redirectUri.GetLeftPart(UriPartial.Authority)}/");
        listener.Start();

        startBrowser(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true });

        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var callback = context.Request.Url;
            AuthorizationResult? authorization;

            try
            {
                authorization = ValidateCallback(authorizationUri, redirectUri, callback);
            }
            catch (InvalidOperationException)
            {
                await RespondAsync(context.Response, HttpStatusCode.BadRequest, "Invalid OAuth callback.", cancellationToken);
                continue;
            }

            if (authorization is null)
            {
                await RespondAsync(
                    context.Response,
                    HttpStatusCode.OK,
                    "DigitalBrain authorization was denied. You can close this window.",
                    cancellationToken);
                return null;
            }

            await RespondAsync(
                context.Response,
                HttpStatusCode.OK,
                "DigitalBrain authorization completed. You can close this window.",
                cancellationToken);
            return authorization;
        }
    }

    private static AuthorizationResult? ValidateCallback(Uri authorizationUri, Uri redirectUri, Uri? callbackUri)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        ArgumentNullException.ThrowIfNull(redirectUri);

        ArgumentNullException.ThrowIfNull(callbackUri);

        if (!redirectUri.IsLoopback || redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException(
                "The local MCP authorization adapter accepts only an HTTP loopback redirect URI.");
        }

        var expectedState = QueryValue(authorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");
        var callbackState = QueryValue(callbackUri, "state");

        if (!string.Equals(callbackUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal)
            || !string.Equals(callbackState, expectedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The OAuth callback does not match the SDK redirect contract.");
        }

        if (QueryValue(callbackUri, "error") is not null)
        {
            return null;
        }

        return new AuthorizationResult
        {
            Code = QueryValue(callbackUri, "code")
                ?? throw new InvalidOperationException("The OAuth callback contains no authorization code."),
            Iss = QueryValue(callbackUri, "iss"),
        };
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);

            if (string.Equals(Uri.UnescapeDataString(pair[0]), name, StringComparison.Ordinal))
            {
                return pair.Length == 2
                    ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                    : string.Empty;
            }
        }

        return null;
    }

    private static async Task RespondAsync(
        HttpListenerResponse response,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = payload.Length;
        await response.OutputStream.WriteAsync(payload, cancellationToken);
        response.Close();
    }
}
