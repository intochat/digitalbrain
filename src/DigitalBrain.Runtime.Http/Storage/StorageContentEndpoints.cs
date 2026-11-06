using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Http.Surface;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NetHeaders = Microsoft.Net.Http.Headers;

namespace DigitalBrain.Runtime.Http.Storage;

public static class StorageContentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = BrainRoutes.Group(endpoints, "/storage");
        group.MapGet("/{storage}/items/{**rest}", Get);
        group.MapPut("/{storage}/items/{**rest}", Put);
        group.MapPost("/{storage}/items/{**rest}", Ticket);
    }

    private static async Task<IResult> Get(HttpContext http, string brainId, string storage, string rest)
    {
        if (!TryItem(rest, out var item)) { return Results.Text("That file is not here.", statusCode: StatusCodes.Status404NotFound); }
        var gateway = Gateway(http);
        var download = await gateway.Open(http.Request, brainId, storage, item, http.RequestAborted).ConfigureAwait(false);
        if (download.Error is not null) { return new Guarded(download.Error, null); }
        var disposition = Disposition(download.FileName, download.Inline);
        if (download.Ranged is not null)
        {
            return new Guarded(new Ranged(download), disposition);
        }
        NetHeaders.EntityTagHeaderValue? tag = null;
        if (download.ETag is not null && NetHeaders.EntityTagHeaderValue.TryParse(download.ETag, out var parsed)) { tag = parsed; }

        var streamed = Results.Stream(download.Content!, download.ContentType, download.Inline ? null : download.FileName, download.Modified, tag, enableRangeProcessing: download.Seekable);
        return new Guarded(streamed, disposition);
    }

    private static Task<IResult> Put(HttpContext http, string brainId, string storage, string rest)
    {
        if (!TryContent(rest, out var folder, out var name) || !StorageContentGateway.SafeName(name))
        {
            return Task.FromResult<IResult>(Results.Text("That location is not available.", statusCode: StatusCodes.Status403Forbidden));
        }
        return Gateway(http).Put(http.Request, brainId, storage, folder, name, http.RequestAborted);
    }

    private static async Task<IResult> Ticket(HttpContext http, string brainId, string storage, string rest)
    {
        if (!TrySuffix(rest, "/upload-ticket", out var folder))
        {
            return Results.Text("That location is not available.", statusCode: StatusCodes.Status403Forbidden);
        }
        var name = http.Request.Query["name"].ToString();
        if (!StorageContentGateway.SafeName(name))
        {
            return Results.Text("That location is not available.", statusCode: StatusCodes.Status403Forbidden);
        }
        string? planId = http.Request.Query["planId"];
        if (string.IsNullOrEmpty(planId) && http.Request.ContentLength is > 0)
        {
            var body = await http.Request.ReadFromJsonAsync<TicketBody>(http.RequestAborted).ConfigureAwait(false);
            planId = body?.PlanId;
        }
        return await Gateway(http).Ticket(brainId, storage, folder, name, planId, http.RequestAborted).ConfigureAwait(false);
    }

    private static StorageContentGateway Gateway(HttpContext http)
        => http.RequestServices.GetRequiredService<StorageContentGateway>();

    private static bool TryItem(string rest, out string item)
    {
        const string Suffix = "/content";
        if (rest.EndsWith(Suffix, StringComparison.Ordinal) && rest.Length > Suffix.Length)
        {
            item = rest[..^Suffix.Length];
            return item.Length > 0;
        }
        item = "";
        return false;
    }

    private static bool TryContent(string rest, out string folder, out string name)
    {
        const string Marker = "/content/";
        var at = rest.LastIndexOf(Marker, StringComparison.Ordinal);
        if (at > 0)
        {
            folder = rest[..at];
            name = rest[(at + Marker.Length)..];
            return folder.Length > 0 && name.Length > 0 && !name.Contains('/', StringComparison.Ordinal);
        }
        folder = "";
        name = "";
        return false;
    }

    private static bool TrySuffix(string rest, string suffix, out string folder)
    {
        if (rest.EndsWith(suffix, StringComparison.Ordinal) && rest.Length > suffix.Length)
        {
            folder = rest[..^suffix.Length];
            if (folder.EndsWith('/')) { folder = folder[..^1]; }
            return folder.Length > 0;
        }
        folder = "";
        return false;
    }

    private static string Disposition(string name, bool inline)
    {
        var cleaned = name.Replace("\"", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        if (cleaned.Length == 0) { cleaned = "download"; }
        return (inline ? "inline" : "attachment") + "; filename=\"" + cleaned + "\"";
    }

    private sealed record TicketBody(string? PlanId);

    private sealed class Ranged(Download download) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var bytes = download.Ranged ?? [];
            httpContext.Response.StatusCode = StatusCodes.Status206PartialContent;
            httpContext.Response.ContentType = download.ContentType;
            httpContext.Response.ContentLength = bytes.Length;
            httpContext.Response.Headers.ContentRange = "bytes " + download.Start + "-" + download.End + "/" + (download.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "*");
            await httpContext.Response.Body.WriteAsync(bytes, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    private sealed class Guarded(IResult inner, string? disposition) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.OnStarting(() =>
            {
                httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
                httpContext.Response.Headers.ContentSecurityPolicy = "sandbox";
                if (disposition is not null) { httpContext.Response.Headers.ContentDisposition = disposition; }
                return Task.CompletedTask;
            });
            await inner.ExecuteAsync(httpContext).ConfigureAwait(false);
        }
    }
}
