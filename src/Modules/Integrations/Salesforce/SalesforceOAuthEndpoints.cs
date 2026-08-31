using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Integrations.Salesforce;

public static class SalesforceOAuthEndpoints
{
    public const string LoginPath = "/integrations/salesforce/login";
    public const string CallbackPath = "/integrations/salesforce/callback";
    private const string Scheme = "SalesforceIntegration";
    internal const string RequestKey = "salesforce-request";

    public static IServiceCollection AddSalesforceBrowserAuthorization(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[IntegrationsModule.SalesforceMcpEndpointConfigurationKey]))
        {
            return services;
        }
        // Framework diagnostics can include remote error descriptions and callback query strings.
        // Keep only our sanitized status messages for this credential boundary.
        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler", LogLevel.None);
            logging.AddFilter(typeof(SalesforceOAuthHandler).FullName, LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        });
        services.AddAuthentication()
            .AddCookie("SalesforceCallback", options => options.Cookie.Name = "db.sf.unused")
            .AddOAuth<OAuthOptions, SalesforceOAuthHandler>(Scheme, options =>
            {
                var settings = new SalesforceOAuthConfiguration(configuration);
                settings.Configure(options);
                options.SignInScheme = "SalesforceCallback";
                options.CallbackPath = CallbackPath;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.Scope.Clear();
                options.Scope.Add("mcp_api");
                options.Scope.Add("refresh_token");
                options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = settings.PublicOrigin.Scheme == "https"
                    ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = async context =>
                    {
                        var request = GetRequest(context.Properties)
                            ?? throw new InvalidOperationException("Salesforce login is missing its original request.");
                        var issuer = context.Request.Query["iss"].ToString();
                        if (issuer.Length != 0 && issuer != "https://login.salesforce.com")
                        {
                            throw new InvalidOperationException("The Salesforce authorization issuer did not match.");
                        }
                        await context.HttpContext.RequestServices.GetRequiredService<SalesforceConnections>()
                            .AcceptTokensAsync(request, context.AccessToken, context.RefreshToken,
                                context.ExpiresIn, CancellationToken.None).ConfigureAwait(false);
                    },
                    OnTicketReceived = async context =>
                    {
                        context.HandleResponse(); // Do not sign in or save tokens to a browser cookie.
                        // The background completion worker resumes the durable request even
                        // when the user closes this tab before the response finishes.
                        await WritePageAsync(context.HttpContext, "Salesforce connected",
                            "You can close this tab. DigitalBrain is continuing your request.", 200).ConfigureAwait(false);
                    },
                    OnRemoteFailure = async context =>
                    {
                        context.HandleResponse();
                        context.HttpContext.RequestServices.GetRequiredService<SalesforceConnections>()
                            .RejectLogin(SalesforceOAuthHandler.VerifiedRequest(context.HttpContext));
                        await WritePageAsync(context.HttpContext, "Salesforce login was not completed",
                            "Return to DigitalBrain and ask again. Check the app callback, scopes and Salesforce permissions if this keeps happening.", 400)
                            .ConfigureAwait(false);
                    },
                    OnAccessDenied = async context =>
                    {
                        context.HandleResponse();
                        context.HttpContext.RequestServices.GetRequiredService<SalesforceConnections>()
                            .RejectLogin(SalesforceOAuthHandler.VerifiedRequest(context.HttpContext));
                        await WritePageAsync(context.HttpContext, "Salesforce login cancelled",
                            "No Salesforce operation was performed. You can close this tab.", 200).ConfigureAwait(false);
                    },
                };
            });
        return services;
    }

    // These two exact paths have their own short-lived request capability / OAuth correlation
    // checks. Run before the Basic gate; all other kernel paths retain their existing protection.
    public static WebApplication UseSalesforceBrowserAuthorization(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[IntegrationsModule.SalesforceMcpEndpointConfigurationKey]))
        {
            return app;
        }
        app.Use(async (context, next) =>
        {
            var isLogin = context.Request.Path.Equals(LoginPath, StringComparison.Ordinal);
            var isCallback = context.Request.Path.Equals(CallbackPath, StringComparison.Ordinal);
            if (!isLogin && !isCallback)
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            SetPrivateResponseHeaders(context);
            RedactCallbackActivity(context);
            var settings = context.RequestServices.GetRequiredService<SalesforceOAuthConfiguration>();
            if (!HttpMethods.IsGet(context.Request.Method)
                || !string.Equals(context.Request.Scheme, settings.PublicOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.Request.Host.Value, settings.PublicOrigin.Authority, StringComparison.OrdinalIgnoreCase))
            {
                await WritePageAsync(context, "Invalid Salesforce login request",
                    "Open the login action from DigitalBrain.", 400).ConfigureAwait(false);
                return;
            }
            try
            {
                if (isLogin)
                {
                    var request = context.Request.Query["request"].ToString();
                    if (!context.RequestServices.GetRequiredService<SalesforceConnections>().TryBegin(request))
                    {
                        await WritePageAsync(context, "This login link expired or was already opened",
                            "Return to DigitalBrain and request Salesforce access again.", 410).ConfigureAwait(false);
                        return;
                    }
                    var properties = new AuthenticationProperties();
                    properties.Items[RequestKey] = request;
                    await context.ChallengeAsync(Scheme, properties).ConfigureAwait(false);
                    return;
                }
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                RedactCallbackActivity(context);
            }
        });
        app.UseAuthentication();
        return app;
    }

    private static void RedactCallbackActivity(HttpContext context)
    {
        var activity = Activity.Current;
        activity?.SetTag("url.query", null);
        activity?.SetTag("http.target", context.Request.Path.Value);
        activity?.SetTag("url.full", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
    }

    private static string? GetRequest(AuthenticationProperties? properties)
        => properties?.Items.TryGetValue(RequestKey, out var request) == true ? request : null;

    private static void SetPrivateResponseHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
    }

    private static Task WritePageAsync(HttpContext context, string title, string message, int status)
    {
        SetPrivateResponseHeaders(context);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        // Only fixed application strings are rendered. Never reflect an OAuth response or URL.
        return context.Response.WriteAsync($"<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>{title}</title><body style=\"font:18px system-ui;max-width:640px;margin:12vh auto;padding:24px\"><h1>{title}</h1><p>{message}</p></body></html>", context.RequestAborted);
    }
}
