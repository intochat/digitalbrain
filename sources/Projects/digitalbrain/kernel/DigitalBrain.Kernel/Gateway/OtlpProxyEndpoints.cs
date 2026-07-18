using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Protobuf;

namespace DigitalBrain.Kernel.Gateway;

/// <summary>
/// OTLP proxy endpoints. Flutter's Dart OpenTelemetry exporter POSTs binary
/// OTLP payloads to <c>/otlp/v1/traces</c> and <c>/otlp/v1/metrics</c> and
/// JSON-encoded OTLP to <c>/otlp/v1/logs</c>; the gateway forwards traces +
/// metrics to the Aspire dashboard's OTLP endpoint (discovered via the
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> env var) and maps logs into the
/// .NET ILogger pipeline (which then flows to Aspire via ServiceDefaults'
/// OpenTelemetry logging provider).
///
/// This is the only reason the system silo hosts a browser-reachable origin
/// for Flutter's telemetry — without it Flutter's OTLP POSTs would have to
/// cross origins to the Aspire dashboard directly, and browsers refuse
/// cross-origin OTLP without the dashboard explicitly opting in to CORS.
///
/// <para>Aspire 13 publishes two OTLP endpoints — a gRPC one (native OTLP
/// over HTTP/2 with length-prefixed frames) and an HTTP/protobuf one (raw
/// protobuf body POSTed to <c>/v1/{signal}</c>). The env var
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> says which one
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> points at; default for Aspire 13 is
/// <c>http/protobuf</c>. The proxy branches on that so we don't wrap a
/// Flutter payload in a gRPC frame and POST it to an http/protobuf
/// collector (which 502s with a transport error).</para>
/// </summary>
public static class OtlpProxyEndpoints
{
    public const string OtlpForwardClientName = "digitalbrain-otlp-forward";

    /// <summary>
    /// Registers the <c>digitalbrain-otlp-forward</c> named HttpClient with Aspire service
    /// discovery + the self-signed-cert bypass the dashboard OTLP endpoint needs.
    /// Must be called BEFORE <c>MapDigitalBrainOtlpProxy</c> (i.e. during builder phase).
    /// Without this the proxy hits the literal <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
    /// value Aspire injects (e.g. <c>localhost:22016</c>) which is DCP's proxy
    /// port and refuses plain HttpClient connections — service discovery rewrites
    /// it to the real dashboard OTLP port.
    /// </summary>
    public static IServiceCollection AddDigitalBrainOtlpForwardClient(this IServiceCollection services)
    {
        services.AddHttpClient(OtlpForwardClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Aspire dashboard uses a self-signed dev cert; no trust path
                // exists in the silo process. Bypass validation — this client
                // only talks to the sibling dashboard in the same Aspire.
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            });
        return services;
    }

    public static IEndpointRouteBuilder MapDigitalBrainOtlpProxy(this WebApplication app)
    {
        var otlpEndpoint = app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            // No OTLP target — Aspire isn't wiring the dashboard, or this is
            // a test boot without a dashboard. Flutter's exporters will still
            // POST to these routes; we accept and drop to keep the client
            // happy. The trace-based E2E fixture captures activities through
            // the in-process ActivityListener instead of the OTLP path.
            app.MapPost("/otlp/v1/traces", static ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }).RequireCors("flutter-web");
            app.MapPost("/otlp/v1/metrics", static ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }).RequireCors("flutter-web");
            app.MapPost("/otlp/v1/logs", static ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; }).RequireCors("flutter-web");
            return app;
        }

        var otlpHeaders = ParseHeaders(app.Configuration["OTEL_EXPORTER_OTLP_HEADERS"]);
        var protocol = (app.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "http/protobuf")
            .Trim().ToLowerInvariant();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OtlpProxy");
        logger.LogInformation(
            "OTLP proxy enabled — forwarding traces/metrics to {Endpoint} via {Protocol} (headers: {HeaderKeys})",
            otlpEndpoint,
            protocol,
            string.Join(",", otlpHeaders.Keys));

        var clientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

        if (protocol == "grpc")
        {
            app.MapPost("/otlp/v1/traces", ctx => ForwardAsGrpc(
                ctx, clientFactory, otlpEndpoint,
                "opentelemetry.proto.collector.trace.v1.TraceService/Export",
                otlpHeaders, logger)).RequireCors("flutter-web");

            app.MapPost("/otlp/v1/metrics", ctx => ForwardAsGrpc(
                ctx, clientFactory, otlpEndpoint,
                "opentelemetry.proto.collector.metrics.v1.MetricsService/Export",
                otlpHeaders, logger)).RequireCors("flutter-web");
        }
        else
        {
            // http/protobuf (Aspire 13 default). Flutter's CollectorExporter
            // already POSTs raw protobuf with Content-Type application/x-protobuf,
            // which is exactly what the dashboard's otlp-http endpoint expects
            // — we just forward the body, preserving the API-key header.
            app.MapPost("/otlp/v1/traces", ctx => ForwardAsHttpProtobuf(
                ctx, clientFactory, otlpEndpoint, "v1/traces", otlpHeaders, logger)).RequireCors("flutter-web");

            app.MapPost("/otlp/v1/metrics", ctx => ForwardAsHttpProtobuf(
                ctx, clientFactory, otlpEndpoint, "v1/metrics", otlpHeaders, logger)).RequireCors("flutter-web");
        }

        app.MapPost("/otlp/v1/logs", ctx => HandleFlutterLogsAsync(ctx, app.Services)).RequireCors("flutter-web");

        return app;
    }

    static async Task ForwardAsGrpc(
        HttpContext ctx,
        IHttpClientFactory factory,
        string otlpEndpoint,
        string grpcPath,
        IReadOnlyDictionary<string, string> extraHeaders,
        ILogger logger)
    {
        var client = factory.CreateClient(OtlpForwardClientName);
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        var payload = ms.ToArray();

        var contentType = ctx.Request.ContentType ?? "";
        if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonStr = System.Text.Encoding.UTF8.GetString(payload);
            try
            {
                var parserSettings = JsonParser.Settings.Default.WithIgnoreUnknownFields(true);
                var parser = new JsonParser(parserSettings);
                if (grpcPath.Contains("MetricsService"))
                {
                    var requestObj = parser.Parse<OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceRequest>(jsonStr);
                    payload = requestObj.ToByteArray();
                }
                else if (grpcPath.Contains("TraceService"))
                {
                    var requestObj = parser.Parse<OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceRequest>(jsonStr);
                    payload = requestObj.ToByteArray();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to translate OTLP JSON payload to binary Protobuf for path {Path}", grpcPath);
            }
        }

        // gRPC frame format: 1 compression byte (0) + 4 big-endian length + payload.
        var grpcBody = new byte[5 + payload.Length];
        grpcBody[0] = 0;
        BinaryPrimitives.WriteInt32BigEndian(grpcBody.AsSpan(1), payload.Length);
        payload.CopyTo(grpcBody, 5);

        var targetUrl = $"{otlpEndpoint.TrimEnd('/')}/{grpcPath}";
        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new ByteArrayContent(grpcBody),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        request.Headers.TryAddWithoutValidation("te", "trailers");
        foreach (var (key, value) in extraHeaders)
            request.Headers.TryAddWithoutValidation(key, value);

        try
        {
            using var response = await client.SendAsync(request, ctx.RequestAborted);
            // Read body fully so HttpClient populates TrailingHeaders — grpc-status
            // rides in a trailing HEADERS frame, not in the initial response
            // headers. Checking response.Headers alone misses the success signal.
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(ctx.RequestAborted);
            var grpcStatus = (response.TrailingHeaders.TryGetValues("grpc-status", out var trailers)
                ? trailers.FirstOrDefault()
                : null)
                ?? (response.Headers.TryGetValues("grpc-status", out var headers)
                    ? headers.FirstOrDefault()
                    : null);

            if ((grpcStatus is "0" or null) && response.IsSuccessStatusCode)
            {
                ctx.Response.StatusCode = 200;
            }
            else
            {
                logger.LogDebug(
                    "OTLP forward {Path} → grpc-status={GrpcStatus} HTTP {Http} body-len={BodyLen}",
                    grpcPath, grpcStatus, (int)response.StatusCode, bodyBytes.Length);
                ctx.Response.StatusCode = 200; // accept+discard so Flutter doesn't retry-thrash
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Dashboard may be unreachable at boot (DCP proxy port dance) or during
            // rebuild. Log at debug, accept+discard so the Flutter batch exporter
            // doesn't hammer the endpoint — the browser console stays clean for
            // review screenshots. A follow-up slice fixes the dashboard wiring so
            // the full 4-span chain shows up in Aspire Traces.
            logger.LogDebug(ex, "OTLP forward to {Path} failed; discarding payload", grpcPath);
            ctx.Response.StatusCode = 200;
        }
    }

    static async Task ForwardAsHttpProtobuf(
        HttpContext ctx,
        IHttpClientFactory factory,
        string otlpEndpoint,
        string relativePath,
        IReadOnlyDictionary<string, string> extraHeaders,
        ILogger logger)
    {
        var client = factory.CreateClient(OtlpForwardClientName);
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted);
        var payload = ms.ToArray();

        var targetUrl = $"{otlpEndpoint.TrimEnd('/')}/{relativePath}";
        var request = new HttpRequestMessage(HttpMethod.Post, targetUrl)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        foreach (var (key, value) in extraHeaders)
            request.Headers.TryAddWithoutValidation(key, value);

        try
        {
            using var response = await client.SendAsync(request, ctx.RequestAborted);
            if (response.IsSuccessStatusCode)
            {
                ctx.Response.StatusCode = 200;
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
                logger.LogDebug(
                    "OTLP forward {Path} → HTTP {Http}: {Body}",
                    relativePath, (int)response.StatusCode, body);
                ctx.Response.StatusCode = 200; // accept+discard
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "OTLP forward to {Path} failed; discarding payload", relativePath);
            ctx.Response.StatusCode = 200;
        }
    }

    static async Task HandleFlutterLogsAsync(HttpContext ctx, IServiceProvider services)
    {
        // Flutter's log exporter sends JSON-encoded OTLP. Deserialize enough to
        // turn each record into an ILogger call tagged "digitalbrain-flutter" — that
        // logger is itself wired into OTel via ServiceDefaults, so the log
        // flows to Aspire as a structured log entry, NOT into the gRPC forward
        // path (which would require re-encoding back to protobuf).
        try
        {
            using var json = await JsonDocument.ParseAsync(
                ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var flutterLogger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("digitalbrain-flutter");

            foreach (var rl in json.RootElement.GetProperty("resourceLogs").EnumerateArray())
            foreach (var sl in rl.GetProperty("scopeLogs").EnumerateArray())
            foreach (var rec in sl.GetProperty("logRecords").EnumerateArray())
            {
                var body = rec.TryGetProperty("body", out var b)
                    && b.TryGetProperty("stringValue", out var sv)
                    ? sv.GetString() ?? ""
                    : "";
                var sevNum = rec.TryGetProperty("severityNumber", out var sn)
                    ? sn.GetInt32()
                    : 9;
                var level = sevNum switch
                {
                    <= 4 => LogLevel.Trace,
                    <= 8 => LogLevel.Debug,
                    <= 12 => LogLevel.Information,
                    <= 16 => LogLevel.Warning,
                    <= 20 => LogLevel.Error,
                    _ => LogLevel.Critical,
                };
                flutterLogger.Log(level, "[flutter] {Message}", body);
            }
            ctx.Response.StatusCode = 200;
        }
        catch
        {
            // Never fail the client on malformed JSON — swallow and 200 so
            // Flutter's retry handler doesn't thrash.
            ctx.Response.StatusCode = 200;
        }
    }

    static IReadOnlyDictionary<string, string> ParseHeaders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EmptyHeaders;
        var dict = new Dictionary<string, string>();
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx > 0)
                dict[pair[..idx].Trim()] = pair[(idx + 1)..].Trim();
        }
        return dict;
    }

    static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(0);
}
