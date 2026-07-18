using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ino.Gateway.Grpc.Endpoints;

/// <summary>
/// <c>GET /config.json</c> — serves the <see cref="InoClientConfig"/> as a
/// CDN-cacheable JSON asset. Populated from <see cref="IOptions{InoClientConfig}"/>
/// so deployments can override the defaults via configuration
/// (<c>appsettings.json</c>, environment, Azure App Configuration, etc.).
///
/// Cache behaviour:
///   * <c>ETag</c> is a deterministic SHA-256 of the serialized JSON. Two
///     identical configs (same body → same ETag) ride the CDN edge forever.
///   * <c>Cache-Control: public, max-age=300, stale-while-revalidate=60</c> —
///     5 min fresh + 1 min SWR. Covers deploy rollouts without stalling the
///     client, and handles flap-induced revalidation gracefully.
///   * <c>If-None-Match</c> on the request short-circuits to 304 without
///     sending the body.
/// </summary>
public static class ConfigEndpoint
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteHandlerBuilder MapInoConfigEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/config.json", (
            HttpContext ctx,
            IOptions<InoClientConfig> options) =>
        {
            // Resolve relative-path defaults against the requesting client's
            // origin so every client (web / desktop / Telegram mini-app) sees
            // absolute URLs it can actually connect to without guessing.
            // TransportSecure mirrors the inbound scheme — Flutter's
            // grpc-dart channel uses TLS iff the page is HTTPS.
            var baseUri = new Uri($"{ctx.Request.Scheme}://{ctx.Request.Host.Value}");
            var resolved = options.Value with
            {
                GrpcEndpoint = ResolveAgainst(options.Value.GrpcEndpoint, baseUri),
                OtlpEndpoint = ResolveAgainst(options.Value.OtlpEndpoint, baseUri),
                HealthEndpoint = ResolveAgainst(options.Value.HealthEndpoint, baseUri),
                TransportSecure = ctx.Request.IsHttps,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(resolved, Json);
            var etag = ComputeETag(bytes);

            // Strong revalidation — If-None-Match short-circuits the body.
            if (ctx.Request.Headers.IfNoneMatch == etag)
            {
                ctx.Response.StatusCode = StatusCodes.Status304NotModified;
                ctx.Response.Headers.ETag = etag;
                ctx.Response.Headers.CacheControl = CacheControl;
                return Task.CompletedTask;
            }

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers.ETag = etag;
            ctx.Response.Headers.CacheControl = CacheControl;
            ctx.Response.ContentLength = bytes.Length;
            return ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted).AsTask();
        });
    }

    const string CacheControl = "public, max-age=300, stale-while-revalidate=60";

    static string ResolveAgainst(string path, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(path)) return baseUri.ToString();
        // Absolute URLs pass through unchanged — deployers can pin clients to
        // a specific host (multi-origin, CDN, etc.) via configuration.
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)) return absolute.ToString();
        // Relative path — join onto the request origin.
        return new Uri(baseUri, path).ToString();
    }

    static string ComputeETag(ReadOnlySpan<byte> body)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(body, hash);
        // Weak "W/" prefix — see RFC 7232 §2.3. Strong ETag requires
        // byte-for-byte equivalence; the serialized JSON is deterministic
        // per configuration instance so strong would work, but weak tolerates
        // benign future changes (whitespace, field ordering) without forcing
        // clients to re-download.
        return "W/\"" + Convert.ToHexString(hash[..16]) + "\"";
    }
}
