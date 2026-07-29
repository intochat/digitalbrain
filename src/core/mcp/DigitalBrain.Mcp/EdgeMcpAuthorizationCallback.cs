using ModelContextProtocol.Authentication;

namespace DigitalBrain.Mcp;

internal static class EdgeMcpAuthorizationCallback
{
    internal static async Task<AuthorizationResult?> AuthorizeAsync(
        AuthorizationCallbackContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var state = QueryValue(context.AuthorizationUri, "state")
            ?? throw new InvalidOperationException("The OAuth authorization URI contains no state value.");
        var delivered = await McpAuthorizationCodeHub.AwaitAsync(state, cancellationToken);
        if (delivered is null)
        {
            return null;
        }

        return new AuthorizationResult
        {
            Code = delivered.Code,
            State = state,
            Iss = delivered.Iss,
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
