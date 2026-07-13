using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.RuntimeHost;
using DigitalBrain.Salesforce;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans;

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
            builder.Services.AddDigitalBrainGoogle();
            builder.Services.AddDigitalBrainSalesforce();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddHealthChecks()
                .AddAsyncCheck("google-connector", static _ => Task.FromResult(
                    HealthCheckResult.Healthy("Google connector is registered")))
                .AddAsyncCheck("salesforce-connector", static _ => Task.FromResult(
                    HealthCheckResult.Healthy("Salesforce connector is registered")));
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;
                if (builder.Environment.IsProduction() && string.Equals(
                        builder.Configuration["DigitalBrain:Runtime:ForwardedHeaders:TrustAzureContainerAppsIngress"],
                        "true",
                        StringComparison.OrdinalIgnoreCase))
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
            app.UseMiddleware<OAuthTransportBoundary>();
            app.UseRouting();
            app.MapDefaultEndpoints();
            app.UseCors("browser");
            MapStaticWebBundle(app);
            MapConnectorOAuthCallbacks(app);
            return app;
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
                    var grpcPorts = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? string.Empty)
                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var grpcPort in grpcPorts)
                    {
                        if (int.TryParse(grpcPort, out var grpcEndpointPort) &&
                            (!hasWebEndpoint || grpcEndpointPort != webEndpointPort))
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
            app.MapGet("/oauth/start/{provider}", async (
                string provider,
                HttpRequest request,
                IServiceProvider services) =>
            {
                SetOAuthResponseHeaders(request.HttpContext.Response);
                var target = (request.Path.Value ?? string.Empty) + (request.QueryString.Value ?? string.Empty);
                if (!OAuthCallbackPaths.IsSupportedProvider(provider) ||
                    !OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out var flowReference))
                    return Results.StatusCode(StatusCodes.Status400BadRequest);

                var protector = services.GetRequiredService<IOAuthStateProtector>();
                if (!protector.TryUnprotect(flowReference, out var owner))
                    return Results.StatusCode(StatusCodes.Status400BadRequest);

                var cluster = services.GetRequiredService<IClusterClient>();
                using var startDeadline = CreateServerOperationDeadline(services);
                if (string.Equals(provider, OAuthCallbackPaths.GoogleProvider, StringComparison.Ordinal))
                {
                    var googleResult = await cluster
                        .GetGrain<IGmailReadToolGrain>(owner.Value)
                        .BeginAuthorizationAsync(flowReference, startDeadline.Token);
                    return googleResult.Status == GmailReadStatus.NeedsAuth &&
                           GoogleClientFactory.IsAllowedAuthorizationUrl(googleResult.ConnectionUrl)
                        ? Results.Redirect(googleResult.ConnectionUrl!, permanent: false, preserveMethod: false)
                        : Results.StatusCode(StatusCodes.Status400BadRequest);
                }

                var salesforceResult = await cluster
                    .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                    .BeginAuthorizationAsync(flowReference, startDeadline.Token);
                return salesforceResult.Status == SalesforceReadStatus.NeedsAuth &&
                       SalesforceClientFactory.IsAllowedAuthorizationUrl(salesforceResult.ConnectionUrl)
                    ? Results.Redirect(salesforceResult.ConnectionUrl!, permanent: false, preserveMethod: false)
                    : Results.StatusCode(StatusCodes.Status400BadRequest);
            });

            app.MapGet("/oauth/callback/{provider}", async (
                string provider,
                HttpRequest request,
                IServiceProvider services) =>
            {
                SetOAuthResponseHeaders(request.HttpContext.Response);
                if (!OAuthCallbackPaths.IsSupportedProvider(provider)) return Results.NotFound();

                var callback = new OAuthCallback(
                    Code: request.Query["code"].FirstOrDefault() ?? string.Empty,
                    State: request.Query["state"].FirstOrDefault() ?? string.Empty,
                    Error: request.Query["error"].FirstOrDefault(),
                    ErrorDescription: request.Query["error_description"].FirstOrDefault());
                AuthResult result;
                if (string.Equals(provider, OAuthCallbackPaths.SalesforceProvider, StringComparison.Ordinal))
                {
                    var protector = services.GetRequiredService<IOAuthStateProtector>();
                    if (!protector.TryUnprotect(callback.State, out var owner))
                    {
                        result = new AuthResult(false, "invalid-state");
                    }
                    else
                    {
                        var cluster = services.GetRequiredService<IClusterClient>();
                        using var completionDeadline = CreateServerOperationDeadline(services);
                        result = await cluster
                            .GetGrain<ISalesforceReadToolGrain>(owner.Value)
                            .CompleteAuthorizationAsync(callback, completionDeadline.Token);
                    }
                }
                else
                {
                    var protector = services.GetRequiredService<IOAuthStateProtector>();
                    if (!protector.TryUnprotect(callback.State, out var owner))
                    {
                        result = new AuthResult(false, "invalid-state");
                    }
                    else
                    {
                        var cluster = services.GetRequiredService<IClusterClient>();
                        using var completionDeadline = CreateServerOperationDeadline(services);
                        result = await cluster
                            .GetGrain<IGmailReadToolGrain>(owner.Value)
                            .CompleteAuthorizationAsync(callback, completionDeadline.Token);
                    }
                }

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
    }

    public sealed class OAuthTransportBoundary(
        RequestDelegate next,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        ILogger<OAuthTransportBoundary> logger)
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
