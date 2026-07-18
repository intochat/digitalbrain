using System.Net.Http;
using System.Net.Http.Headers;

namespace DigitalBrain.Kernel.Runtime;

public interface IInoPackageLoader
{
    Task<string> DownloadFromUrlAsync(string url, CancellationToken ct = default);
}

public sealed class InoPackageLoader(IHttpClientFactory httpClientFactory, string? personalAccessToken = null) : IInoPackageLoader
{
    public async Task<string> DownloadFromUrlAsync(string url, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient("InoPackageLoader");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        if (!string.IsNullOrWhiteSpace(personalAccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", personalAccessToken);
        }
        
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("InoPackageLoader", "1.0"));

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync(ct);
    }
}
