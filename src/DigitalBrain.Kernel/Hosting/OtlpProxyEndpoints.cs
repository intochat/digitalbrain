using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DigitalBrain.Kernel;

internal static class OtlpProxyEndpoints
{
    private const string OtlpForwardClientName = "digitalbrain-otlp-forward";

    public static IServiceCollection AddDigitalBrainOtlpForwardClient(this IServiceCollection services)
    {
        // Keep the platform certificate chain; telemetry must not be forwarded to
        // an endpoint with an untrusted certificate.
        services.AddHttpClient(OtlpForwardClientName);
        return services;
    }

    public static IEndpointRouteBuilder MapDigitalBrainOtlpProxy(this WebApplication app)
    {
        var otlpEndpoint = app.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            app.MapPost("/otlp/v1/traces", static context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
            app.MapPost("/otlp/v1/metrics", static context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
            app.MapPost("/otlp/v1/logs", static context => { context.Response.StatusCode = 200; return Task.CompletedTask; });
            return app;
        }

        var otlpHeaders = ParseHeaders(app.Configuration["OTEL_EXPORTER_OTLP_HEADERS"]);
        var protocol = (app.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "http/protobuf")
            .Trim()
            .ToLowerInvariant();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DigitalBrain.OtlpProxy");
        var clientFactory = app.Services.GetRequiredService<IHttpClientFactory>();

        logger.LogInformation(
            "OTLP proxy enabled: forwarding Flutter traces and metrics to {Endpoint} via {Protocol}.",
            otlpEndpoint,
            protocol);

        if (protocol == "grpc")
        {
            app.MapPost("/otlp/v1/traces", context => ForwardAsGrpcAsync(
                context,
                clientFactory,
                otlpEndpoint,
                "opentelemetry.proto.collector.trace.v1.TraceService/Export",
                otlpHeaders,
                logger));

            app.MapPost("/otlp/v1/metrics", context => ForwardAsGrpcAsync(
                context,
                clientFactory,
                otlpEndpoint,
                "opentelemetry.proto.collector.metrics.v1.MetricsService/Export",
                otlpHeaders,
                logger));
        }
        else
        {
            app.MapPost("/otlp/v1/traces", context => ForwardAsHttpProtobufAsync(
                context,
                clientFactory,
                otlpEndpoint,
                "v1/traces",
                otlpHeaders,
                logger));

            app.MapPost("/otlp/v1/metrics", context => ForwardAsHttpProtobufAsync(
                context,
                clientFactory,
                otlpEndpoint,
                "v1/metrics",
                otlpHeaders,
                logger));
        }

        app.MapPost("/otlp/v1/logs", context => HandleFlutterLogsAsync(context, app.Services));

        return app;
    }

    private static async Task ForwardAsGrpcAsync(
        HttpContext context,
        IHttpClientFactory factory,
        string otlpEndpoint,
        string grpcPath,
        IReadOnlyDictionary<string, string> extraHeaders,
        ILogger logger)
    {
        var client = factory.CreateClient(OtlpForwardClientName);
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        var payload = buffer.ToArray();

        var grpcBody = new byte[5 + payload.Length];
        grpcBody[0] = 0;
        BinaryPrimitives.WriteInt32BigEndian(grpcBody.AsSpan(1), payload.Length);
        payload.CopyTo(grpcBody, 5);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{otlpEndpoint.TrimEnd('/')}/{grpcPath}")
        {
            Content = new ByteArrayContent(grpcBody),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        request.Headers.TryAddWithoutValidation("te", "trailers");
        foreach (var (key, value) in extraHeaders)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        try
        {
            using var response = await client.SendAsync(request, context.RequestAborted);
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(context.RequestAborted);
            var grpcStatus = (response.TrailingHeaders.TryGetValues("grpc-status", out var trailers)
                ? trailers.FirstOrDefault()
                : null)
                ?? (response.Headers.TryGetValues("grpc-status", out var headers)
                    ? headers.FirstOrDefault()
                    : null);

            if ((grpcStatus is "0" or null) && response.IsSuccessStatusCode)
            {
                context.Response.StatusCode = 200;
                return;
            }

            logger.LogDebug(
                "OTLP forward {Path} failed with grpc-status={GrpcStatus}, HTTP {StatusCode}, body length {BodyLength}.",
                grpcPath,
                grpcStatus,
                (int)response.StatusCode,
                bodyBytes.Length);
            context.Response.StatusCode = 200;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "OTLP gRPC forward to {Path} failed; discarding payload.", grpcPath);
            context.Response.StatusCode = 200;
        }
    }

    private static async Task ForwardAsHttpProtobufAsync(
        HttpContext context,
        IHttpClientFactory factory,
        string otlpEndpoint,
        string relativePath,
        IReadOnlyDictionary<string, string> extraHeaders,
        ILogger logger)
    {
        var client = factory.CreateClient(OtlpForwardClientName);
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{otlpEndpoint.TrimEnd('/')}/{relativePath}")
        {
            Content = new ByteArrayContent(buffer.ToArray()),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        foreach (var (key, value) in extraHeaders)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        try
        {
            using var response = await client.SendAsync(request, context.RequestAborted);
            if (response.IsSuccessStatusCode)
            {
                context.Response.StatusCode = 200;
                return;
            }

            var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
            logger.LogDebug(
                "OTLP forward {Path} failed with HTTP {StatusCode}: {Body}",
                relativePath,
                (int)response.StatusCode,
                body);
            context.Response.StatusCode = 200;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "OTLP HTTP/protobuf forward to {Path} failed; discarding payload.", relativePath);
            context.Response.StatusCode = 200;
        }
    }

    private static async Task HandleFlutterLogsAsync(HttpContext context, IServiceProvider services)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                cancellationToken: context.RequestAborted);
            var flutterLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("digitalbrain-flutter");

            foreach (var resourceLog in document.RootElement.GetProperty("resourceLogs").EnumerateArray())
            {
                foreach (var scopeLog in resourceLog.GetProperty("scopeLogs").EnumerateArray())
                {
                    foreach (var record in scopeLog.GetProperty("logRecords").EnumerateArray())
                    {
                        var body = record.TryGetProperty("body", out var bodyElement)
                            && bodyElement.TryGetProperty("stringValue", out var stringValue)
                                ? stringValue.GetString() ?? string.Empty
                                : string.Empty;
                        var severity = record.TryGetProperty("severityNumber", out var severityElement)
                            ? severityElement.GetInt32()
                            : 9;
                        var level = severity switch
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
                }
            }

            context.Response.StatusCode = 200;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            context.Response.StatusCode = 200;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return EmptyHeaders;
        }

        var headers = new Dictionary<string, string>();
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index > 0)
            {
                headers[pair[..index].Trim()] = pair[(index + 1)..].Trim();
            }
        }

        return headers;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>(0);
}
