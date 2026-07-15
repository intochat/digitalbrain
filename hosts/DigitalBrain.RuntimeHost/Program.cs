using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.RuntimeHost;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainRuntimeHost();
var app = builder.Build();
app.MapDigitalBrainRuntimeHost();
app.Run();
namespace DigitalBrain.RuntimeHost
{
    public static class RuntimeHostExtensions
    {
        public static WebApplicationBuilder AddDigitalBrainRuntimeHost(this WebApplicationBuilder builder)
        {
            builder.AddServiceDefaults();
            builder.UseDigitalBrainOrleans();
            builder.AddDigitalBrainClients();
            builder.AddKeyedAzureBlobServiceClient("features", settings =>
            {
                settings.DisableHealthChecks = true;
                settings.DisableTracing = true;
            });
            var corsOrigins = builder.Configuration.GetSection("DigitalBrain:Cors:AllowedOrigins").Get<string[]>()
                ?? new[] { "https://digitalbrain.tech", "https://www.digitalbrain.tech" };
            builder.Services.AddCors(options => options.AddPolicy("browser", policy => policy.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader()));
            builder.Services.AddDigitalBrainGoogle();
            builder.Services.AddDigitalBrainSalesforce();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IFeaturePublicationVerifier, BlobFeaturePublicationVerifier>();
            builder.Services.AddHealthChecks().AddAsyncCheck("google-connector", static _ => Task.FromResult(HealthCheckResult.Healthy("Google connector is registered")))
                .AddAsyncCheck("salesforce-connector", static _ => Task.FromResult(HealthCheckResult.Healthy("Salesforce connector is registered")));
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                if (builder.Environment.IsProduction() && string.Equals(builder.Configuration["DigitalBrain:Runtime:ForwardedHeaders:TrustAzureContainerAppsIngress"], "true", StringComparison.OrdinalIgnoreCase))
                {
                    options.KnownIPNetworks.Clear();
                    options.KnownProxies.Clear();
                }
            });
            ConfigureKestrel(builder);
            return builder;
        }
        public static WebApplication MapDigitalBrainRuntimeHost(this WebApplication app)
        {
            app.UseForwardedHeaders();
            app.UseMiddleware<FeatureCapabilityTransportBoundary>();
            app.UseMiddleware<OAuthTransportBoundary>();
            app.UseRouting();
            app.MapDefaultEndpoints();
            app.UseCors("browser");
            MapStaticWebBundle(app);
            MapConnectorOAuthCallbacks(app);
            MapFeatureCapabilities(app);
            return app;
        }
        private static void MapFeatureCapabilities(WebApplication app)
        {
            app.MapPost("/internal/features/capabilities/execute", async (HttpContext httpContext, CapabilityRequest request, ICapabilityDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                if (!httpContext.Items.ContainsKey(FeatureCapabilityTransportBoundary.AuthenticatedItem))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                try
                {
                    var result = await dispatcher.ExecuteAsync(request, cancellationToken);
                    return Results.Json(new FeatureCapabilityResponse(result.Kind.ToString(), result.Payload));
                }
                catch (CapabilityDeniedException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }).DisableAntiforgery();
        }
        private static bool FixedTimeEquals(string? expected, string? supplied)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)) return false;
            var expectedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expected));
            var suppliedHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(supplied));
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        }
        public sealed class FeatureCapabilityTransportBoundary(RequestDelegate next, IConfiguration configuration, TimeProvider timeProvider)
        {
            internal const string AuthenticatedItem = "digitalbrain.feature-capability.authenticated";
            private const int RequestsPerMinute = 240;
            private const long MaximumBodyBytes = 70 * 1024;
            private readonly SemaphoreSlim concurrency = new(16, 16);
            private readonly object rateGate = new();
            private DateTimeOffset windowStartedAt = timeProvider.GetUtcNow();
            private int windowCount;
            public async Task InvokeAsync(HttpContext context)
            {
                if (!context.Request.Path.Equals("/internal/features/capabilities/execute"))
                {
                    await next(context);
                    return;
                }
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }
                var expected = configuration["DigitalBrain:FeatureHost:InternalToken"];
                var supplied = context.Request.Headers["X-DigitalBrain-Internal-Token"].FirstOrDefault();
                if (!FixedTimeEquals(expected, supplied))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                if (context.Request.ContentLength is > MaximumBodyBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                var bodySize = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = MaximumBodyBytes;
                if (!TryTakeRateSlot() || !await concurrency.WaitAsync(0, context.RequestAborted))
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return;
                }
                try
                {
                    context.Items[AuthenticatedItem] = true;
                    await next(context);
                }
                finally
                {
                    concurrency.Release();
                }
            }
            private bool TryTakeRateSlot()
            {
                lock (rateGate)
                {
                    var now = timeProvider.GetUtcNow();
                    if (now - windowStartedAt >= TimeSpan.FromMinutes(1))
                    {
                        windowStartedAt = now;
                        windowCount = 0;
                    }
                    if (windowCount >= RequestsPerMinute) return false;
                    windowCount++;
                    return true;
                }
            }
        }
        private static void ConfigureKestrel(WebApplicationBuilder builder)
        {
            var isAspireHosted = DigitalBrainHostEnvironment.IsAspireHosted(builder.Configuration);
            builder.WebHost.ConfigureKestrel(options =>
            {
                if (isAspireHosted)
                {
                    var webPort = Environment.GetEnvironmentVariable("DIGITALBRAIN_WEB_PORT");
                    var hasWebEndpoint = int.TryParse(webPort, out var webEndpointPort);
                    var grpcPorts = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var grpcPort in grpcPorts)
                    {
                        if (int.TryParse(grpcPort, out var grpcEndpointPort) && (!hasWebEndpoint || grpcEndpointPort != webEndpointPort))
                        {
                            options.ListenAnyIP(grpcEndpointPort, listen => listen.Protocols = HttpProtocols.Http2);
                        }
                    }
                    if (hasWebEndpoint)
                    {
                        options.ListenAnyIP(webEndpointPort, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
                    }
                    return;
                }
                options.ListenAnyIP(8080, listen => listen.Protocols = HttpProtocols.Http2);
                options.ListenAnyIP(8081, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
            });
        }
        private static void MapStaticWebBundle(WebApplication app)
        {
            var webRoot = app.Configuration["DIGITALBRAIN_WEBROOT"];
            var serveWebBundle = !string.IsNullOrWhiteSpace(webRoot) && Directory.Exists(webRoot);
            if (!serveWebBundle)
            {
                return;
            }
            var absoluteWebRoot = Path.GetFullPath(webRoot!);
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(absoluteWebRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
            var indexPath = Path.Combine(absoluteWebRoot, "index.html");
            app.MapFallback(async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
            });
        }
        private static void MapConnectorOAuthCallbacks(WebApplication app)
        {
            app.MapGet("/oauth/start/{provider}", async (string provider, HttpRequest request, IServiceProvider services) =>
            {
                SetOAuthResponseHeaders(request.HttpContext.Response);
                var target = (request.Path.Value ?? string.Empty) + (request.QueryString.Value ?? string.Empty);
                var authorization = services.GetServices<IExternalAuthorizationResolver>()
                    .SingleOrDefault(candidate => string.Equals(candidate.Provider, provider, StringComparison.Ordinal));
                if (authorization is null || !OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out var flowReference))
                    return Results.StatusCode(StatusCodes.Status400BadRequest);
                var protector = services.GetRequiredService<IOAuthStateProtector>();
                if (!protector.TryUnprotect(flowReference, out var owner))
                    return Results.StatusCode(StatusCodes.Status400BadRequest);
                var connector = services.GetRequiredKeyedService<IConnector>(provider);
                using var startDeadline = CreateServerOperationDeadline(services);
                var challenge = await connector.BeginAuthAsync(owner, cancellationToken: startDeadline.Token);
                return !challenge.IsForm && authorization.IsAllowedAuthorizationUrl(challenge.UrlOrForm)
                    ? Results.Redirect(challenge.UrlOrForm, permanent: false, preserveMethod: false)
                    : Results.StatusCode(StatusCodes.Status400BadRequest);
            });
            app.MapGet("/oauth/callback/{provider}", async (string provider, HttpRequest request, IServiceProvider services) =>
            {
                SetOAuthResponseHeaders(request.HttpContext.Response);
                if (!services.GetServices<IExternalAuthorizationResolver>()
                    .Any(candidate => string.Equals(candidate.Provider, provider, StringComparison.Ordinal))) return Results.NotFound();
                var callback = new OAuthCallback(
                    Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                    State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                    Error: request.Query["error"].FirstOrDefault(),
                    ErrorDescription: request.Query["error_description"].FirstOrDefault());
                var connector = services.GetRequiredKeyedService<IConnector>(provider);
                using var completionDeadline = CreateServerOperationDeadline(services);
                var result = await connector.CompleteAuthAsync(callback, completionDeadline.Token);
                var title = result.Success ? "Connection complete" : "Connection not completed";
                var message = result.Success
                    ? "You can return to DigitalBrain. INO will resume your request automatically."
                    : result.Error switch
                    {
                        "consent-denied" => "Consent was denied. No connection was created.",
                        "invalid-state" or "state-mismatch" or "no-pending" => "This authorization request is invalid or expired. Start again from DigitalBrain.",
                        "no-code" => "The authorization response was incomplete. Start again from DigitalBrain.",
                        _ => "The provider connection could not be completed. Start again from DigitalBrain."
                    };
                return Results.Content(
                    $"<html><body><h1>{title}</h1><p>{message}</p></body></html>",
                    "text/html",
                    statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
            });
        }
        private static CancellationTokenSource CreateServerOperationDeadline(IServiceProvider services)
        {
            var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            return deadline;
        }
        private static void SetOAuthResponseHeaders(HttpResponse response)
        {
            response.Headers.CacheControl = "no-store";
            response.Headers.Pragma = "no-cache";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            response.Headers.XContentTypeOptions = "nosniff";
        }
        private sealed record FeatureCapabilityResponse(string Kind, System.Text.Json.JsonElement Payload);
    }
    public sealed class OAuthTransportBoundary(RequestDelegate next, IHostEnvironment environment, TimeProvider timeProvider, ILogger<OAuthTransportBoundary> logger)
    {
        private const int RequestsPerMinute = 120;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
        private readonly SemaphoreSlim concurrency = new(16, 16);
        private readonly object rateGate = new();
        private DateTimeOffset windowStartedAt = timeProvider.GetUtcNow();
        private int windowCount;
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/oauth"))
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            if (!HttpMethods.IsGet(context.Request.Method) || context.Request.ContentLength is < 0 or > 0)
            {
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }
            if (environment.IsProduction() && !context.Request.IsHttps)
            {
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                return;
            }
            if (!TryTakeRateSlot() || !await concurrency.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(RequestTimeout);
            context.RequestAborted = timeout.Token;
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                }
            }
            catch (Exception exception)
            {
                logger.LogError("OAuth transport request failed with {ExceptionType}.", exception.GetType().Name);
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    return;
                }
                context.Abort();
            }
            finally
            {
                concurrency.Release();
            }
        }
        private bool TryTakeRateSlot()
        {
            lock (rateGate)
            {
                var now = timeProvider.GetUtcNow();
                if (now - windowStartedAt >= TimeSpan.FromMinutes(1))
                {
                    windowStartedAt = now;
                    windowCount = 0;
                }
                if (windowCount >= RequestsPerMinute) return false;
                windowCount++;
                return true;
            }
        }
    }
}
