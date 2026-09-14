using System.Net;
using System.Text.Json;

namespace DigitalBrain.Coding;

public sealed class HttpSlotEndpoints(IHttpClientFactory clients) : ISlotEndpoints
{
    public const string HttpClientName = "digitalbrain-slots";

    public async Task<bool> HealthyAsync(string slotUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, "/health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // A slot that is not listening yet is not a failure; the reaction looks again.
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // The smoke read is a [ReadOnly] neuron read over HTTP, which is exactly what the fence lets a standby
    // answer: it proves the new slot activated its brain without writing anything.
    public async Task<string?> SmokeAsync(string slotUrl, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, path), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? null
                : $"the smoke read {path} on {slotUrl} answered {(int)response.StatusCode}";
        }
        catch (HttpRequestException error)
        {
            return $"the smoke read {path} on {slotUrl} failed: {error.Message}";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"the smoke read {path} on {slotUrl} timed out";
        }
    }

    public async Task<bool> HoldsLeaseAsync(string slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, "/slots/" + Uri.EscapeDataString(slot)), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("holdsLease", out var holds) && holds.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // One failure type, so the promotion's reaction has one thing to catch and one thing to report; a 429
    // is not a failure but a wait the gateway is asking for.
    public async Task<TimeSpan?> SwitchAsync(string gatewayUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.PostAsync(Address(gatewayUrl, "/switch/" + Uri.EscapeDataString(slot)), content: null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
                    ?? TimeSpan.FromSeconds(1);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"the gateway at {gatewayUrl} refused the switch to '{slot}' with {(int)response.StatusCode}");
            }

            return null;
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException($"the gateway at {gatewayUrl} could not be reached: {error.Message}", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"the gateway at {gatewayUrl} did not answer the switch to '{slot}' in time", error);
        }
    }

    public async Task<string?> ActiveAsync(string gatewayUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayUrl);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(gatewayUrl, "/active"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("active", out var active) ? active.GetString() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri Address(string root, string path) => new(new Uri(root, UriKind.Absolute), path);
}
