using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DigitalBrain.Coding;

// One send path for every probe, so a slot that is not listening, a slot that answers with something other
// than JSON and a gateway that never answers all become a value the promotion can act on. Only the switch
// turns a failure into an exception, because a promotion that cannot move traffic has nothing to report.
public sealed class HttpSlotEndpoints(HttpClient client) : ISlotEndpoints
{
    public const string HttpClientName = "digitalbrain-slots";

    // A 429 with no usable header, a zero delta or a date already in the past would all become a busy
    // loop, so the wait the gateway asks for is never shorter than this.
    private static readonly TimeSpan MinimumRetry = TimeSpan.FromSeconds(1);

    public Task<bool> HealthyAsync(Uri slotUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slotUrl);
        return SendAsync(HttpMethod.Get, Address(slotUrl, "/health"),
            static (response, _) => Task.FromResult(response.IsSuccessStatusCode),
            // A slot that is not listening yet is not a failure; the reaction looks again.
            static (_, _) => false,
            cancellationToken);
    }

    // The smoke read is a [ReadOnly] neuron read over HTTP, which is exactly what the fence lets a standby
    // answer: it proves the new slot activated its brain without writing anything.
    public Task<string?> SmokeAsync(Uri slotUrl, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return SendAsync<string?>(HttpMethod.Get, Address(slotUrl, path),
            (response, _) => Task.FromResult<string?>(response.IsSuccessStatusCode
                ? null
                : $"the smoke read {path} on {slotUrl} answered {(int)response.StatusCode}"),
            (reason, _) => $"the smoke read {path} on {slotUrl} failed: {reason}",
            cancellationToken);
    }

    public Task<bool> HoldsLeaseAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return SendAsync(HttpMethod.Get, LeaseAddress(slotUrl, slot),
            async (response, token) =>
            {
                using var document = await ReadJsonAsync(response, token).ConfigureAwait(false);
                // The silo names the slot it is: an answer from a silo that is not this slot, or from one
                // that carries no slot name at all, is never evidence that this slot's flip has landed.
                return document is not null
                    && TextOf(document.RootElement, "slot") is { } named
                    && string.Equals(named, slot, StringComparison.OrdinalIgnoreCase)
                    && document.RootElement.TryGetProperty("holdsLease", out var holds)
                    && holds.ValueKind == JsonValueKind.True;
            },
            static (_, _) => false,
            cancellationToken);
    }

    public Task<string?> NamedSlotAsync(Uri slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return SendAsync<string?>(HttpMethod.Get, LeaseAddress(slotUrl, slot),
            async (response, token) =>
            {
                using var document = await ReadJsonAsync(response, token).ConfigureAwait(false);
                return document is null ? null : TextOf(document.RootElement, "slot");
            },
            // A gated silo answers 401 and an unreachable one answers nothing; neither is evidence that
            // the slot name is wrong, so both read as "not known" and leave the promotion to its wait.
            static (_, _) => null,
            cancellationToken);
    }

    public Task<TimeSpan?> SwitchAsync(Uri gatewayUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        return SendAsync(HttpMethod.Post, Address(gatewayUrl, "/switch/" + Uri.EscapeDataString(slot)),
            (response, _) =>
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    return Task.FromResult<TimeSpan?>(RetryAfter(response.Headers.RetryAfter));
                }

                return response.IsSuccessStatusCode
                    ? Task.FromResult<TimeSpan?>(null)
                    : throw new InvalidOperationException($"the gateway at {gatewayUrl} refused the switch to '{slot}' with {(int)response.StatusCode}");
            },
            (reason, error) => throw new InvalidOperationException($"the gateway at {gatewayUrl} did not accept the switch to '{slot}': {reason}", error),
            cancellationToken);
    }

    public Task<string?> ActiveAsync(Uri gatewayUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gatewayUrl);
        return SendAsync<string?>(HttpMethod.Get, Address(gatewayUrl, "/active"),
            async (response, token) =>
            {
                using var document = await ReadJsonAsync(response, token).ConfigureAwait(false);
                return document is null ? null : TextOf(document.RootElement, "active");
            },
            static (_, _) => null,
            cancellationToken);
    }

    // The one place an HTTP failure becomes a value: reason is what the caller may put in a message, and
    // error is kept so a rethrow does not lose the stack. A cancellation the caller asked for is not a
    // failure and travels on.
    private async Task<T> SendAsync<T>(
        HttpMethod method,
        Uri address,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        Func<string, Exception, T> onFailure,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await read(response, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            return onFailure(error.Message, error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            return onFailure("it did not answer in time", error);
        }
    }

    // Null when the answer was not a success or was not JSON, so no probe has to guard a parse of its own.
    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? TextOf(JsonElement body, string property)
        => body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static TimeSpan RetryAfter(RetryConditionHeaderValue? header)
    {
        var asked = header?.Delta
            ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? MinimumRetry;
        return asked < MinimumRetry ? MinimumRetry : asked;
    }

    private static Uri Address(Uri root, string path) => new(root, path);

    private static Uri LeaseAddress(Uri slotUrl, string slot) => Address(slotUrl, "/slots/" + Uri.EscapeDataString(slot));
}
