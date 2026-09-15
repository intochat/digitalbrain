using System.Net;
using System.Net.Http.Headers;

namespace DigitalBrain.Telegram.Gateway;

/// <summary>A fixed-origin, fixed-route proxy. Generic DigitalBrain APIs never enter this surface.</summary>
public sealed class TelegramGateway
{
    public const int MaxBodyBytes = 65536;
    private readonly HttpClient _client;
    private readonly Uri _kernel;
    private static readonly string[] ResponseHeaders = ["Cache-Control", "ETag", "Last-Modified", "Expires", "Content-Type", "Content-Length", "Content-Encoding"];

    public TelegramGateway(HttpClient client, string kernelUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!Uri.TryCreate(kernelUrl, UriKind.Absolute, out var kernel) || kernel.Scheme is not ("http" or "https") ||
            kernel.AbsolutePath != "/" || kernel.Query.Length != 0 || kernel.Fragment.Length != 0 || kernel.UserInfo.Length != 0)
        {
            throw new ArgumentException("The kernel URL must be an HTTP origin.", nameof(kernelUrl));
        }
        _client = client;
        _kernel = kernel;
    }

    public async Task HandleAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path.Value ?? "";
        if (!IsAllowed(context.Request.Method, path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var asset = path is "/telegram/app" || path.StartsWith("/telegram/app/", StringComparison.Ordinal);
        var webhook = path is "/telegram/webhook" or "/telegram/health";
        var headerName = webhook ? "X-Telegram-Bot-Api-Secret-Token" : "Authorization";
        var credentials = context.Request.Headers[headerName];
        if (!asset && (credentials.Count != 1 || string.IsNullOrWhiteSpace(credentials[0]) ||
            (webhook ? credentials[0]!.Length > 256 : !credentials[0]!.StartsWith("tma ", StringComparison.Ordinal) || credentials[0]!.Length is <= 4 or > 16384)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (path == "/telegram/app")
        {
            context.Response.Redirect("/telegram/app/");
            return;
        }
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }
        // No query strings, Host, cookies, forwarding headers, method overrides, or generic credentials cross this boundary.
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), new Uri(_kernel, path));
        if (!asset)
        {
            request.Headers.TryAddWithoutValidation(headerName, credentials[0]);
        }
        if (HttpMethods.IsPost(context.Request.Method))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > MaxBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                await buffer.WriteAsync(chunk.AsMemory(0, count), context.RequestAborted).ConfigureAwait(false);
            }
            request.Content = new ByteArrayContent(buffer.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.StatusCode != HttpStatusCode.NotModified)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var name in ResponseHeaders)
            {
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                {
                    context.Response.Headers[name] = values.ToArray();
                }
            }
            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            if (!context.Response.HasStarted) { context.Response.StatusCode = StatusCodes.Status502BadGateway; }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted) { context.Response.StatusCode = StatusCodes.Status504GatewayTimeout; }
        }
    }

    public static bool IsAllowed(string method, string path)
    {
        if (path == "/telegram/webhook") { return HttpMethods.IsPost(method); }
        if (path is "/telegram/health" or "/telegram/miniapp/state") { return HttpMethods.IsGet(method); }
        if (path is "/telegram/miniapp/notifications/dismiss" or "/telegram/miniapp/reminders/cancel") { return HttpMethods.IsPost(method); }
        if (path != "/telegram/app" && !path.StartsWith("/telegram/app/", StringComparison.Ordinal)) { return false; }
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) { return false; }
        // Reject encodings and dot segments before System.Uri can normalize an asset into another kernel route.
        return path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '-' or '_') &&
            !path.Contains("//", StringComparison.Ordinal) &&
            !path.Split('/').Any(segment => segment is "." or "..");
    }
}
