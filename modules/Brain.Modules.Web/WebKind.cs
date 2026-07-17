using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Web;

public sealed class WebKind(IHttpClientFactory httpClientFactory) : INeuronKind
{
    private const int MinBytes = 1;
    private const int MaxBytesLimit = 262144;
    private const int DefaultBytes = 65536;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    public string Kind => "web";
    public string[] Contracts => ["web.fetch.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "web.fetch.v1" => HandleFetchAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new { fetches = context.Journal.Count(evt => evt.Kind == "web.fetched") });

    private async ValueTask<KindResult> HandleFetchAsync(NeuronContext context, string inputJson)
    {
        var (uri, requestedMaxBytes) = ParseRequest(inputJson);
        var maxBytes = Math.Clamp(requestedMaxBytes ?? DefaultBytes, MinBytes, MaxBytesLimit);

        using var client = httpClientFactory.CreateClient();
        using var deadline = new CancellationTokenSource(FetchTimeout);

        int status;
        byte[] buffer;
        int bytesRead;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            status = (int)response.StatusCode;
            var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            await using (stream)
            {
                buffer = new byte[maxBytes];
                bytesRead = await ReadBoundedAsync(stream, buffer, deadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new BrainException(BrainErrors.ProviderTimeout, $"fetch of '{uri}' timed out after {FetchTimeout.TotalSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }
        catch (IOException ex)
        {
            throw new BrainException(BrainErrors.ProviderError, ex.Message);
        }

        var body = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        var eventPayload = JsonSerializer.Serialize(new
        {
            urlSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))),
            status,
            bytes = bytesRead,
            body = TruncateUtf8(body, 8192)
        });
        var output = JsonSerializer.Serialize(new { status, body, revision = context.Revision + 1 });

        return new KindResult(output, [("web.fetched", eventPayload)]);
    }

    private static async Task<int> ReadBoundedAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static (Uri Uri, int? MaxBytes) ParseRequest(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "url field is required");

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new BrainException("input.invalid", "url must be an absolute http or https url");

            int? maxBytes = null;
            if (root.TryGetProperty("maxBytes", out var maxBytesElement))
            {
                if (maxBytesElement.ValueKind != JsonValueKind.Number || !maxBytesElement.TryGetInt32(out var parsedMaxBytes))
                    throw new BrainException("input.invalid", "maxBytes must be an integer");
                maxBytes = parsedMaxBytes;
            }

            return (uri, maxBytes);
        }
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
            return value;

        var length = maxBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            length--;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
