using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

// Production: an Azure Container Apps custom-container session pool runs the Sandbox image, one
// Hyper-V session per owner with that owner's scripts side by side. The pool allocates a session on
// the first request carrying a new identifier, so reads check the session exists before touching it.
internal sealed class SessionPoolRunner(HttpClient http, TokenCredential credential, IOptions<CSharpOptions> options) : ICSharpRunner
{
    private const string ManagementApiVersion = "2025-02-02-preview";
    private static readonly TokenRequestContext PoolScope = new(["https://dynamicsessions.io/.default"]);

    private readonly SandboxRunsApi _api = new(http, async cancellationToken
        => (await credential.GetTokenAsync(PoolScope, cancellationToken).ConfigureAwait(false)).Token);

    private Uri Pool => new((options.Value.SessionPoolEndpoint ?? throw new InvalidOperationException("SessionPoolEndpoint is not configured.")).TrimEnd('/') + "/");

    public Task StartAsync(CSharpRun run, CancellationToken cancellationToken)
        => _api.StartAsync(Pool, Session(run.Owner), run, cancellationToken);

    public async Task StopAsync(string owner, string runId, CancellationToken cancellationToken)
    {
        if (runId.Length > 0 && await SessionExistsAsync(owner, cancellationToken).ConfigureAwait(false))
        {
            await _api.StopAsync(Pool, Session(owner), runId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CSharpRunState> InspectAsync(string owner, string runId, CancellationToken cancellationToken)
        => runId.Length > 0 && await SessionExistsAsync(owner, cancellationToken).ConfigureAwait(false)
            ? await _api.InspectAsync(Pool, Session(owner), runId, cancellationToken).ConfigureAwait(false)
            : CSharpRunState.Stopped;

    public async Task<string> LogsAsync(string owner, string runId, int tail, CancellationToken cancellationToken)
        => runId.Length > 0 && await SessionExistsAsync(owner, cancellationToken).ConfigureAwait(false)
            ? await _api.LogsAsync(Pool, Session(owner), runId, tail, cancellationToken).ConfigureAwait(false)
            : "";

    private async Task<bool> SessionExistsAsync(string owner, CancellationToken cancellationToken)
    {
        var url = SandboxRunsApi.Url(Pool, ".management/getSession", Session(owner), "api-version=" + ManagementApiVersion);
        using var response = await _api.SendAsync(HttpMethod.Post, url, content: null, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // Session identifiers allow a narrow character set, and principal ids should not leak into URLs.
    internal static string Session(string owner)
        => "identifier=u-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(owner)))[..32];
}
