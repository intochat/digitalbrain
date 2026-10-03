using DigitalBrain.Platform.Identity.Configuration;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Identity.Directory;

namespace DigitalBrain.Platform.Identity;

public static class IdentityHosting
{
    public static void AddIdentity(this IServiceCollection services)
    {
        services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.SectionName)
            .Validate(options => options.Posture is not null,
                $"Configure {AuthOptions.PostureKey} (Open or Secured) before starting the host.")
            .Validate(options => string.IsNullOrEmpty(options.Username) == string.IsNullOrEmpty(options.Password),
                "A Basic bootstrap credential needs both DigitalBrain:Auth:Username and DigitalBrain:Auth:Password, or neither.")
            .ValidateOnStart();
        services.AddOptions<IdentityHostOptions>().BindConfiguration(IdentityHostOptions.SectionName)
            .Validate(options => options.Validate(), "Identity host names must not be empty.")
            .ValidateOnStart();
        services.AddDataProtection();
        services.AddOptions<Microsoft.AspNetCore.DataProtection.DataProtectionOptions>()
            .Configure<IOptions<IdentityHostOptions>>((protection, host) => protection.ApplicationDiscriminator = host.Value.ProtectionApplicationName);
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
        services.TryAddSingleton<IBrainAccess>(provider =>
        {
            var directory = new DirectoryBrainAccess(provider.GetRequiredService<Orleans.IGrainFactory>());
            return provider.GetRequiredService<IOptions<AuthOptions>>().Value.Posture == IdentityPosture.Open
                ? new OpenOwnerBrainAccess(directory) : directory;
        });
    }

    public static void UsePlatformHttp(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<Directory.IdentityMigrationOptions>>().Value.Maintenance)
        { throw new InvalidOperationException("Maintenance hosts cannot expose Platform HTTP traffic. Run the migration operation without HTTP endpoints."); }
        app.UseKernelCors();
        app.UseModuleHttpSurfaces();
        app.UseAuthentication();
        app.UseAccountSession();
    }
}
