using Ino.Gateway.Grpc.Endpoints;
using Ino.Gateway.Grpc.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ino.Gateway.Grpc;

/// <summary>
/// Wires the gRPC transport + gRPC-Web middleware + (optional) Flutter static
/// file serving into a WebApplication host. In Aspire-hosted production, the
/// host's Kestrel endpoints are controlled by Aspire — this extension only
/// registers services and middleware, it does NOT call
/// <c>WebHost.ConfigureKestrel</c>. Test harnesses that need a dual-endpoint
/// layout (HTTP/2-only for native gRPC + HTTP/1.1+2 for browser) configure
/// Kestrel themselves before calling <see cref="AddInoGrpcGateway"/>.
/// </summary>
public static class InoGrpcHostingExtensions
{
    public static IHostApplicationBuilder AddInoGrpcGateway(this IHostApplicationBuilder builder)
    {
        builder.Services.AddInoGateway();
        // Surface server exception messages in the grpc-status trailer for
        // non-production environments so Flutter can show the real cause
        // instead of the default "Exception was thrown by handler." placeholder.
        // Off in production to avoid leaking stack traces to clients.
        builder.Services.AddGrpc(o =>
            o.EnableDetailedErrors = !builder.Environment.IsProduction());

        builder.Services.AddCors(o => o.AddPolicy("InoGrpcWeb", p => p
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders(
                "Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));

        // Bind InoClientConfig — served by GET /config.json. Property-level
        // defaults in the record cover unset fields; appsettings.json
        // "InoClient" section, env vars "InoClient__*", etc. override. The
        // relative defaults ("/", "/otlp", "/health") are rewritten to
        // absolute URLs by the /config.json handler per-request.
        builder.Services
            .AddOptions<InoClientConfig>()
            .BindConfiguration("InoClient");

        // Named HttpClient the OTLP proxy uses to forward Flutter traces/metrics
        // to the Aspire dashboard. Going through IHttpClientFactory means the
        // factory picks up ServiceDefaults' AddServiceDiscovery + resilience
        // handlers, which rewrite DCP-proxy ports in OTEL_EXPORTER_OTLP_ENDPOINT
        // to the real dashboard ports at call time.
        builder.Services.AddInoOtlpForwardClient();

        return builder;
    }

    /// <summary>
    /// Wire gRPC routing + gRPC-Web middleware + (optional) Flutter SPA
    /// hosting. Call after <c>builder.Build()</c>. The <paramref name="wwwroot"/>
    /// path is resolved relative to the application base directory when
    /// not absolute; when null or missing, the app serves gRPC only.
    /// </summary>
    public static WebApplication UseInoGrpcGateway(this WebApplication app, string? wwwroot = null)
    {
        // gRPC-Web middleware must run before routing so it transforms the
        // application/grpc-web content type into application/grpc before
        // the endpoint routing layer dispatches to the service.
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        app.UseCors("InoGrpcWeb");

        var resolved = ResolveWwwroot(app, wwwroot);
        if (resolved != null)
        {
            var fileProvider = new PhysicalFileProvider(resolved);
            // ASP.NET Core's default content-type map omits Flutter/Rive
            // bundled assets (.riv, .wasm, .json variants are fine). Register
            // .riv explicitly so the persona orb asset isn't served as 404.
            var contentTypes = new FileExtensionContentTypeProvider();
            contentTypes.Mappings[".riv"] = "application/octet-stream";
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = fileProvider,
                ContentTypeProvider = contentTypes,
            });
        }

        app.MapGrpcService<InoGrpcService>().RequireCors("InoGrpcWeb");
        app.MapInoConfigEndpoint();
        app.MapInoOtlpProxy();

        if (resolved != null)
        {
            // SPA fallback must register AFTER gRPC endpoints so the gRPC
            // routes take precedence — otherwise /ino.v1.Ino/Chat would
            // resolve to index.html.
            var indexPath = Path.Combine(resolved, "index.html");
            app.MapFallback(async ctx =>
            {
                if (!File.Exists(indexPath))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }
                ctx.Response.ContentType = "text/html";
                await ctx.Response.Body.WriteAsync(await File.ReadAllBytesAsync(indexPath));
            });
        }

        return app;
    }

    /// <summary>
    /// Resolve the wwwroot path. Absolute paths are used as-is. For relative
    /// paths we try — in order — the host's ContentRoot, the process working
    /// directory, and <see cref="AppContext.BaseDirectory"/>. Aspire launches
    /// silo projects with different roots after <c>rebuild</c> vs initial
    /// start, so probing all three means the static-file + fallback handlers
    /// don't rely on one specific launcher configuration.
    /// </summary>
    static string? ResolveWwwroot(WebApplication app, string? wwwroot)
    {
        if (string.IsNullOrWhiteSpace(wwwroot)) return null;
        if (Path.IsPathRooted(wwwroot))
            return Directory.Exists(wwwroot) ? wwwroot : null;

        var candidates = new[]
        {
            Path.Combine(app.Environment.ContentRootPath, wwwroot),
            Path.Combine(Environment.CurrentDirectory, wwwroot),
            Path.Combine(AppContext.BaseDirectory, wwwroot),
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
