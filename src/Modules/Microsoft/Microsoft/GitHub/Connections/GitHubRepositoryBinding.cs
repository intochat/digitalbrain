using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Microsoft.GitHub;
// Configuration is trusted application input. Never serialize this type or include it in a log.
internal sealed class GitHubRepositoryBinding
{
    private int _revoked;
    internal GitHubRepositoryBinding(string id, long repositoryId, long installationId, long appId, string repoOwner, string repoName, string privateKeyPem, string webhookSecret, string? endpointId = null, Uri? apiHost = null, Uri? mcpEndpoint = null, string? authorizationEpoch = null)
    {
        ValidateName(id);
        ValidateName(endpointId ?? id);
        ValidateName(repoOwner);
        ValidateName(repoName);
        ArgumentOutOfRangeException.ThrowIfLessThan(repositoryId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(installationId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(appId, 1);
        if (string.IsNullOrWhiteSpace(privateKeyPem) || webhookSecret.Length < 16)
        {
            throw new InvalidOperationException("GitHub requires an App private key and a webhook secret of at least 16 characters.");
        }

        Id = id;
        RepositoryId = repositoryId;
        InstallationId = installationId;
        AppId = appId;
        RepoOwner = repoOwner;
        RepoName = repoName;
        PrivateKeyPem = privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal);
        WebhookSecret = webhookSecret;
        EndpointId = endpointId ?? id;
        var validatedApi = ValidateEndpoint(apiHost ?? new Uri("https://api.github.com/"));
        ApiHost = new Uri(validatedApi.AbsoluteUri.TrimEnd('/') + '/');
        McpEndpoint = ValidateEndpoint(mcpEndpoint ?? new Uri("https://api.githubcopilot.com/mcp/"));
        Revision = authorizationEpoch ?? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Id}|{RepositoryId}|{InstallationId}|{AppId}|{RepoOwner}|{RepoName}|{ApiHost}|{McpEndpoint}|{privateKeyPem}")));
    }

    public string Id { get; }
    public long RepositoryId { get; }
    public long InstallationId { get; }
    public long AppId { get; }
    public string RepoOwner { get; }
    public string RepoName { get; }
    public Uri ApiHost { get; }
    public Uri McpEndpoint { get; }
    public string EndpointId { get; }
    public string Revision { get; }
    public bool Enabled => Volatile.Read(ref _revoked) == 0;
    internal string PrivateKeyPem { get; }
    internal string WebhookSecret { get; }
    internal string RepositoryPath => $"repos/{Uri.EscapeDataString(RepoOwner)}/{Uri.EscapeDataString(RepoName)}";

    public void Revoke() => Interlocked.Exchange(ref _revoked, 1);
    internal void RequireEnabled()
    {
        if (!Enabled)
        {
            throw new GitHubAccessDeniedException("The configured GitHub repository is revoked.");
        }
    }

    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value is "." or ".." || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new InvalidOperationException("GitHub binding and repository names must use letters, digits, '.', '-' or '_'.");
        }
    }

    private static Uri ValidateEndpoint(Uri value)
    {
        if (!value.IsAbsoluteUri || value.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(value.UserInfo) || !string.IsNullOrEmpty(value.Query) || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new InvalidOperationException("GitHub endpoints must be absolute HTTPS URLs without embedded credentials, queries or fragments.");
        }

        return value;
    }
}
