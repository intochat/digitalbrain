using System.Net;
using Brain.Contracts;

namespace Brain.Modules.Connections;

public static class ConnectionHttp
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public static async Task<string> SendThrowingAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new BrainException(BrainErrors.ProviderTimeout, $"{request.RequestUri} timed out after {RequestTimeout.TotalSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }
        catch (IOException ex)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new BrainException(BrainErrors.ProviderError, $"{(int)response.StatusCode}: {body}");
            return body;
        }
    }

    public static async Task<ProbeResult> ProbeGetAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            using var response = await client.SendAsync(request, linked.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new ProbeResult(ConnectionHealth.TokenExpired, $"probe returned {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
                return new ProbeResult(ConnectionHealth.ProviderError, $"probe returned {(int)response.StatusCode}");
            return new ProbeResult(ConnectionHealth.Healthy, "probe succeeded");
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new ProbeResult(ConnectionHealth.NetworkError, $"probe timed out after {RequestTimeout.TotalSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            return new ProbeResult(ConnectionHealth.NetworkError, ex.Message);
        }
        catch (IOException ex)
        {
            return new ProbeResult(ConnectionHealth.NetworkError, ex.Message);
        }
    }
}
