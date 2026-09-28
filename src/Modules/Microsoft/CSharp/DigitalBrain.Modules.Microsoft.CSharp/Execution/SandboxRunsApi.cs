using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DigitalBrain.Microsoft.CSharp;

// The Sandbox project's run API. Production reaches the same API through the session pool, which
// adds its own ?identifier= to pick the session, so the run id always travels in the path.
internal sealed class SandboxRunsApi(HttpClient http, Func<CancellationToken, ValueTask<string?>> bearer)
{
    public async Task StartAsync(Uri sandbox, string session, CSharpRun run, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, Url(sandbox, $"runs/{run.RunId}", session),
            JsonContent.Create(new SandboxRunRequest(run.Source, new(run.Environment, StringComparer.Ordinal))), cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "start", cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(Uri sandbox, string session, string runId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, Url(sandbox, $"runs/{runId}/stop", session), content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) { await EnsureSuccessAsync(response, "stop", cancellationToken).ConfigureAwait(false); }
    }

    public async Task<CSharpRunState> InspectAsync(Uri sandbox, string session, string runId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, Url(sandbox, $"runs/{runId}", session), content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) { return CSharpRunState.Stopped; }
        await EnsureSuccessAsync(response, "inspect", cancellationToken).ConfigureAwait(false);
        var run = await response.Content.ReadFromJsonAsync<SandboxRunStatus>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The sandbox returned an empty run status.");
        return new(run.Status == "Running" ? CSharpFileStatus.Running : CSharpFileStatus.Exited, run.ExitCode, run.StartedAt);
    }

    public async Task<string> LogsAsync(Uri sandbox, string session, string runId, int tail, CancellationToken cancellationToken)
    {
        var lines = Math.Clamp(tail, 1, 5000).ToString(CultureInfo.InvariantCulture);
        using var response = await SendAsync(HttpMethod.Get, Url(sandbox, $"runs/{runId}/logs", session, "tail=" + lines), content: null, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : "";
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (await bearer(cancellationToken).ConfigureAwait(false) is { } token) { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); }
        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public static Uri Url(Uri sandbox, string path, string session, string? query = null)
    {
        var parameters = string.Join('&', new[] { session, query }.Where(part => !string.IsNullOrEmpty(part)));
        return new Uri(sandbox, parameters.Length == 0 ? path : path + "?" + parameters);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) { return; }
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"The sandbox could not {action} the script ({(int)response.StatusCode}): {detail}");
    }
}
