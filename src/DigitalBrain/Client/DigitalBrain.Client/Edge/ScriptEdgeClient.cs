using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts.Edge.V1;

namespace DigitalBrain.Client;

internal sealed class ScriptEdgeClient(HttpClient http)
{
    public async Task<JsonElement?> InvokeAsync(ScriptInvocation invocation, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(ScriptEdgeProtocol.Invoke, invocation, ScriptEdgeProtocol.Json, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, invocation.Contract + "." + invocation.Method, cancellationToken).ConfigureAwait(false);
        return response.Content.Headers.ContentLength == 0 || response.StatusCode == System.Net.HttpStatusCode.NoContent
            ? null
            : await response.Content.ReadFromJsonAsync<JsonElement>(ScriptEdgeProtocol.Json, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> OpenSignalsAsync(string contract, string key, string signal, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(ScriptSignalQuery.SignalsQuery(contract, key, signal), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, "subscribe to " + signal, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) { return; }
        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.Headers.TryGetValues(ScriptEdgeProtocol.RefusalHeader, out var codes))
        { throw new ScriptRefusalException(detail, codes.Single()); }
        throw new InvalidOperationException($"The brain refused {operation} ({(int)response.StatusCode}): {detail}");
    }
}
