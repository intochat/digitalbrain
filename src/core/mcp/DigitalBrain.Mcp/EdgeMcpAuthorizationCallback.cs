using System.Net.Http;
using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class EdgeMcpAuthorizationCallback
{
    internal static Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
        => AuthorizeAsync(context, ambient: null, cancellationToken);

    internal static async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        McpAuthorizationAmbientState? ambient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var state = QueryValue(context.AuthorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");

        if (ambient is not null)
        {
            // 1) Surface the real provider authorize URL so the emitting grain journals
            //    AuthorizationRequired under its capability context.
            // 2) Wait for Begin so DeliverCallback has a pending record.
            // 3) Follow authorize → real edge callback as the browser would.
            // 4) Return the code for the SDK token exchange.
            McpAuthorizationCodeHub.RegisterAmbient(state, ambient);
            ambient.SignInReady.TrySetResult(new McpAuthorizationSignIn(context.AuthorizationUri, state));
            await ambient.BeginCompleted.Task.WaitAsync(cancellationToken);

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CheckCertificateRevocationList = true,
            };
            using var browser = new HttpClient(handler, disposeHandler: true);
            using var authorize = await browser.GetAsync(context.AuthorizationUri, cancellationToken);
            var location = authorize.Headers.Location
                ?? throw new InvalidOperationException("The OAuth authorize endpoint did not redirect.");
            if (!location.IsAbsoluteUri)
            {
                location = new Uri(context.AuthorizationUri, location);
            }

            using var edge = await browser.GetAsync(location, cancellationToken);
            _ = edge;

            var error = QueryValue(location, "error");
            if (!string.IsNullOrWhiteSpace(error))
            {
                return null;
            }

            var code = QueryValue(location, "code")
                ?? throw new InvalidOperationException("The OAuth callback redirect contained no code.");
            var iss = QueryValue(location, "iss");
            return new AuthorizationResult
            {
                Code = code,
                State = state,
                Iss = iss,
            };
        }

        var hubDelivered = await McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        if (hubDelivered is null)
        {
            return null;
        }

        return new AuthorizationResult
        {
            Code = hubDelivered.Code,
            State = state,
            Iss = hubDelivered.Iss,
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
}
