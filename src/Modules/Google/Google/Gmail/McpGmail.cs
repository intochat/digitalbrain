using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Sdk;

namespace DigitalBrain.Google;

internal sealed class McpGmail(GmailMcp gmail, GmailLogins logins) : IGmail
{
    public async Task<string> SearchJsonAsync(
        OwnerId owner,
        string account,
        string topic,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            ' ',
            new[] { string.IsNullOrWhiteSpace(account) ? "" : $"from:{account}", topic }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        try
        {
            var result = await gmail.CallAsync(
                owner, "search_threads",
                new Dictionary<string, object?> { ["query"] = query, ["pageSize"] = 3, ["pageToken"] = "", ["includeTrash"] = false, ["view"] = "THREAD_VIEW_MINIMAL" },
                cancellationToken).ConfigureAwait(false);
            return result.GetRawText();
        }
        catch (McpAuthenticationRequiredException)
        {
            logins.RequireLogin(compose: false, cancellationToken);
            throw;
        }
    }
}
