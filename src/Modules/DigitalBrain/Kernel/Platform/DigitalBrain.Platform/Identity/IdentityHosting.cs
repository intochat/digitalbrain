using DigitalBrain.Platform.Identity.Configuration;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Platform.Identity;

public static class IdentityHosting
{
    internal static void AddIdentity(this IServiceCollection services)
    {
        services.AddOptions<BasicAuthOptions>().BindConfiguration(BasicAuthOptions.SectionName);
        services.AddOptions<IdentityHostOptions>().BindConfiguration(IdentityHostOptions.SectionName)
            .Validate(options => options.Validate(), "Identity host names must not be empty.")
            .ValidateOnStart();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<IOptions<IdentityHostOptions>>((cookie, host) =>
            {
                cookie.Cookie.Name = host.Value.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.SlidingExpiration = true;
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        services.AddKernelCors();
    }

    public static void UsePlatformHttp(this WebApplication app)
    {
        app.UseKernelCors();
        app.UseModuleHttpSurfaces();
        app.UseAuthentication();
        app.UseAccountSession();
    }
}
