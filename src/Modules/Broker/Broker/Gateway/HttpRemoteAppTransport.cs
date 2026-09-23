using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

// Speaks the publisher-hosted MCP-over-HTTP endpoint. It forwards only the operation and the data
// class names the broker already authorized; no caller identity or secret leaves the silo.
internal sealed class HttpRemoteAppTransport : IRemoteAppTransport
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async ValueTask<RemoteAppResponse> InvokeAsync(
        AppManifest manifest, RemoteCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(manifest.RemoteEndpoint))
        {
            return new RemoteAppResponse { Succeeded = false, Output = "The remote app declares no endpoint." };
        }

        var payload = new RemoteCallPayload(manifest.Id, request.Operation, request.DataClasses);
        try
        {
            using var response = await HttpClient
                .PostAsJsonAsync(manifest.RemoteEndpoint, payload, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new RemoteAppResponse { Succeeded = false, Output = $"Remote endpoint returned {(int)response.StatusCode}." };
            }

            var body = await response.Content
                .ReadFromJsonAsync<RemoteAppPayload>(cancellationToken)
                .ConfigureAwait(false);
            return body is null
                ? new RemoteAppResponse { Succeeded = false, Output = "Remote endpoint returned an empty body." }
                : new RemoteAppResponse
                {
                    Succeeded = body.Succeeded,
                    Output = body.Output,
                    UsedDataClasses = body.UsedDataClasses ?? [],
                    UsedMeters = body.UsedMeters ?? [],
                };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new RemoteAppResponse { Succeeded = false, Output = $"Remote endpoint call failed: {exception.Message}" };
        }
    }

    private sealed record RemoteCallPayload(string AppId, string Operation, IReadOnlyList<string> DataClasses);

    private sealed record RemoteAppPayload(
        bool Succeeded,
        string? Output,
        IReadOnlyList<string>? UsedDataClasses,
        IReadOnlyList<string>? UsedMeters);
}
