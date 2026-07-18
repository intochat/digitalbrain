using System.Net.Http.Json;
using System.Text.Json;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;

namespace TripRadar.MiniApp.Client.Infrastructure.Http;

public sealed class TripRadarApiClient(HttpClient http, IAuthTokenProvider auth)
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T?> GraphQlAsync<T>(string query, object? variables = null, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.GraphQl);
        ApplyAuth(request);
        request.Content = JsonContent.Create(new { query, variables }, options: WriteOptions);

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GraphQlResponse<T>>(ReadOptions, ct);
        if (result?.Errors is { Count: > 0 })
            throw new InvalidOperationException(result.Errors[0].Message);

        return result is not null ? result.Data : default;
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);

        return await response.Content.ReadFromJsonAsync<T>(ReadOptions);
    }

    public async Task<T?> PostAsync<T>(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(request);
        request.Content = JsonContent.Create(body, options: WriteOptions);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);

        return await response.Content.ReadFromJsonAsync<T>(ReadOptions);
    }

    public async Task PostAsync(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        ApplyAuth(request);
        request.Content = JsonContent.Create(body, options: WriteOptions);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);
    }

    public async Task<T?> PutAsync<T>(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url);
        ApplyAuth(request);
        request.Content = JsonContent.Create(body, options: WriteOptions);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);

        return await response.Content.ReadFromJsonAsync<T>(ReadOptions);
    }

    public async Task DeleteAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        ApplyAuth(request);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);
    }

    public async Task PatchAsync(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        ApplyAuth(request);
        request.Content = JsonContent.Create(body, options: WriteOptions);

        var response = await http.SendAsync(request);
        await EnsureSuccessOrThrowApiErrorAsync(response);
    }

    private static async Task EnsureSuccessOrThrowApiErrorAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        // server returns ErrorResponse { errorCode, errorReason } on failure
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(ReadOptions);
            if (!string.IsNullOrEmpty(error?.ErrorReason))
                throw new HttpRequestException(error.ErrorReason);
        }
        catch (HttpRequestException) { throw; }
        catch { /* deserialization failed — fall through */ }

        response.EnsureSuccessStatusCode();
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (auth.Token is { } token)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-Client-Type", "api");
    }

    private sealed record GraphQlResponse<T>(T Data, List<GraphQlError>? Errors);
    private sealed record GraphQlError(string Message);
    private sealed record ApiErrorResponse(string? ErrorCode, string? ErrorReason);
}