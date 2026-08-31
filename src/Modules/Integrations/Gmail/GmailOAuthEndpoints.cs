using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DigitalBrain.Integrations.Gmail;

public static class GmailOAuthEndpoints
{
    public const string LoginPath = "/integrations/gmail/login";
    public const string CallbackPath = "/integrations/gmail/callback";
    internal const string RequestKey = "gmail-request";
    private const string Scheme = "GmailIntegration";
    private static readonly object TokensKey = new();

    public static IServiceCollection AddGmailBrowserAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        // Register authentication even when Gmail isn't configured, so Kernel has one explicit
        // middleware after BOTH providers' pre-authentication request guards.
        var auth = services.AddAuthentication();
        if (IntegrationsModule.UseFakeTransports(configuration))
        {
            return services;
        }

        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OpenIdConnect", LogLevel.None);
            logging.AddFilter(typeof(GmailOAuthHandler).FullName, LogLevel.None);
            logging.AddFilter("Microsoft.IdentityModel", LogLevel.None);
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        });
        auth.AddScheme<OpenIdConnectOptions, GmailOAuthHandler>(Scheme, options =>
        {
            var settings = new GmailOAuthConfiguration(configuration);
            // Placeholders avoid startup failure; pre-auth guard refuses any challenge until configured.
            options.ClientId = settings.IsConfigured ? settings.ClientId : "not-configured";
            options.ClientSecret = settings.IsConfigured ? settings.ClientSecret : "not-configured";
            options.Authority = "https://accounts.google.com";
            options.CallbackPath = CallbackPath;
            options.SignInScheme = "GmailNeverSignIn";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.MapInboundClaims = false;
            options.GetClaimsFromUserInfoEndpoint = false;
            options.RequireHttpsMetadata = true;
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(10);
            options.BackchannelTimeout = TimeSpan.FromSeconds(30);
            options.BackchannelHttpHandler = new HttpClientHandler { AllowAutoRedirect = false };
            options.Scope.Clear();
            options.Scope.Add("openid"); options.Scope.Add("email"); options.Scope.Add(GmailOAuthConfiguration.ReadScope);
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
                ValidateAudience = true,
                ValidAudience = options.ClientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
            options.ProtocolValidator.RequireNonce = true;
            options.CorrelationCookie.HttpOnly = true;
            options.NonceCookie.HttpOnly = true;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            var secure = settings.IsConfigured && settings.PublicOrigin.Scheme == "https" ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.CorrelationCookie.SecurePolicy = secure; options.NonceCookie.SecurePolicy = secure;
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    settings.RequireConfigured();
                    context.ProtocolMessage.RedirectUri = new Uri(settings.PublicOrigin, CallbackPath).AbsoluteUri;
                    context.ProtocolMessage.SetParameter("include_granted_scopes", "true");
                    context.ProtocolMessage.SetParameter("access_type", "offline");
                    context.ProtocolMessage.Prompt = "consent"; // Guarantees refresh consent on reconnect/incremental access.
                    if (context.Properties.Items.TryGetValue("gmail-compose", out var compose) && compose == "true")
                    {
                        context.ProtocolMessage.Scope += " " + GmailOAuthConfiguration.ComposeScope;
                    }

                    return Task.CompletedTask;
                },
                OnTokenResponseReceived = context =>
                {
                    if (!string.Equals(context.TokenEndpointResponse.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new GmailOperationException("Google returned an unsupported token type.");
                    }
                    // HttpContext only. Never put access/refresh/ID tokens in tickets or AuthenticationProperties.
                    context.HttpContext.Items[TokensKey] = new Tokens(context.TokenEndpointResponse.AccessToken,
                        context.TokenEndpointResponse.RefreshToken, context.TokenEndpointResponse.Scope, context.TokenEndpointResponse.ExpiresIn);
                    return Task.CompletedTask;
                },
                OnTicketReceived = async context =>
                {
                    context.HandleResponse(); // Deliberately no sign-in handler or persistent authentication cookie.
                    var request = GmailOAuthHandler.VerifiedRequest(context.HttpContext);
                    try
                    {
                        var principal = context.Principal;
                        var sub = principal?.FindFirst("sub")?.Value;
                        var email = principal?.FindFirst("email")?.Value;
                        if (request is null || string.IsNullOrWhiteSpace(sub) || sub.Length > 256
                            || string.IsNullOrWhiteSpace(email) || email.Length > 320
                            || !string.Equals(principal?.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                            || !context.HttpContext.Items.Remove(TokensKey, out var value) || value is not Tokens tokens)
                        {
                            throw new GmailOperationException("Google identity validation was incomplete.");
                        }

                        var connections = context.HttpContext.RequestServices.GetRequiredService<GmailConnections>();
                        await context.HttpContext.RequestServices.GetRequiredService<GmailPendingActions>().AcceptAsync(request,
                            (owner, compose, valid) => connections.AcceptAsync(owner, sub, email, tokens.AccessToken,
                                tokens.RefreshToken, tokens.Scope, tokens.ExpiresIn, compose, valid, context.HttpContext.RequestAborted)).ConfigureAwait(false);
                        await PageAsync(context.HttpContext, "Gmail connected. You can close this tab and return to DigitalBrain. Login did not create a draft.", 200).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        context.HttpContext.RequestServices.GetRequiredService<GmailPendingActions>().Reject(request);
                        await PageAsync(context.HttpContext, "Gmail login could not be completed. Check required scopes and Google identity, then ask again in DigitalBrain.", 400).ConfigureAwait(false);
                    }
                    finally { context.HttpContext.Items.Remove(TokensKey); }
                },
                OnRemoteFailure = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.Items.Remove(TokensKey);
                    context.HttpContext.RequestServices.GetRequiredService<GmailPendingActions>().Reject(GmailOAuthHandler.VerifiedRequest(context.HttpContext));
                    await PageAsync(context.HttpContext, "Gmail login failed or was cancelled. Return to DigitalBrain and check callback registration, account access and required scopes.", 400).ConfigureAwait(false);
                },
                OnAccessDenied = async context =>
                {
                    context.HandleResponse();
                    context.HttpContext.RequestServices.GetRequiredService<GmailPendingActions>().Reject(GmailOAuthHandler.VerifiedRequest(context.HttpContext));
                    await PageAsync(context.HttpContext, "Gmail login was cancelled. No Gmail operation was performed.", 200).ConfigureAwait(false);
                },
            };
        });
        // AddScheme does not apply OpenIdConnect's essential post-configuration (state/data protection).
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>();
        return services;
    }

    public static WebApplication UseGmailBrowserAuthorization(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var login = context.Request.Path.Equals(LoginPath, StringComparison.Ordinal);
            if (!login && !context.Request.Path.Equals(CallbackPath, StringComparison.Ordinal)) { await next(context).ConfigureAwait(false); return; }
            PrivateHeaders(context);
            Redact(context);
            var settings = new GmailOAuthConfiguration(app.Configuration);
            if (!settings.IsConfigured)
            { await PageAsync(context, "Gmail setup is incomplete. Configure ClientId, ClientSecret and PublicOrigin privately in Aspire.", 503).ConfigureAwait(false); return; }
            if (!HttpMethods.IsGet(context.Request.Method)
                || !string.Equals(context.Request.Scheme, settings.PublicOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.Request.Host.Value, settings.PublicOrigin.Authority, StringComparison.OrdinalIgnoreCase))
            { await PageAsync(context, "Invalid Gmail login request. Open the login action from DigitalBrain.", 400).ConfigureAwait(false); return; }
            try
            {
                if (login)
                {
                    var request = context.Request.Query["request"];
                    if (context.Request.Query.Count != 1 || request.Count != 1
                        || !context.RequestServices.GetRequiredService<GmailPendingActions>().TryBegin(request[0], out var compose))
                    { await PageAsync(context, "This Gmail login link expired or was already opened. Ask again in DigitalBrain.", 410).ConfigureAwait(false); return; }
                    var properties = new AuthenticationProperties();
                    properties.Items[RequestKey] = request[0];
                    properties.Items["gmail-compose"] = compose ? "true" : "false";
                    await context.ChallengeAsync(Scheme, properties).ConfigureAwait(false);
                }
                else
                {
                    await next(context).ConfigureAwait(false);
                }
            }
            catch (Exception)
            { await PageAsync(context, "Gmail authorization is unavailable. Check the private OAuth configuration and try a new request.", 503).ConfigureAwait(false); }
            finally { Redact(context); }
        });
        return app;
    }
    private static void Redact(HttpContext context)
    {
        Activity.Current?.SetTag("url.query", null);
        Activity.Current?.SetTag("http.target", context.Request.Path.Value);
        Activity.Current?.SetTag("url.full", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}");
    }
    private static void PrivateHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    }
    private static Task PageAsync(HttpContext context, string message, int status)
    {
        PrivateHeaders(context); context.Response.StatusCode = status; context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync(message, context.RequestAborted);
    }
    private sealed class Tokens(string accessToken, string? refreshToken, string scope, string? expiresIn)
    {
        internal readonly string AccessToken = accessToken;
        internal readonly string? RefreshToken = refreshToken;
        internal readonly string Scope = scope;
        internal readonly string? ExpiresIn = expiresIn;
    }
}
