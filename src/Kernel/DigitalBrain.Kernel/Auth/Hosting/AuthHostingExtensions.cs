using Azure.Data.Tables;
using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Kernel;

internal static class AuthHostingExtensions
{
    public const string AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static IHostApplicationBuilder AddDigitalBrainAuth(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(static services =>
        {
            var tables = services.GetRequiredKeyedService<TableServiceClient>(DigitalBrainResourceNames.Clustering);
            return tables.GetTableClient(AuthOptions.UsersTableName);
        });
        builder.Services.TryAddSingleton<IAccountDirectory, TableAccountDirectory>();
        builder.Services.TryAddSingleton<IWorkspaceMembershipGateway, WorkspaceMembershipGateway>();
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddIdentityCore<DigitalBrainUser>(static options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddUserStore<DigitalBrainUserStore>();

        builder.Services.AddScoped<IUserClaimsPrincipalFactory<DigitalBrainUser>, DigitalBrainClaimsPrincipalFactory>();

        builder.Services
            .AddAuthentication(AuthenticationScheme)
            .AddCookie(AuthenticationScheme, static options =>
            {
                options.Cookie.Name = AuthOptions.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.Events.OnRedirectToLogin = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization(static options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        builder.Services.AddSingleton(static services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var environment = services.GetRequiredService<IHostEnvironment>();
            return new LoopbackDevAuthOptions(
                AuthOptions.ResolveAllowLoopbackDev(configuration, environment));
        });
        builder.Services.AddHostedService<DevelopmentBootstrapSeeder>();

        return builder;
    }

    public static WebApplication UseDigitalBrainAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<HttpsStanceMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<LoopbackDevAuthMiddleware>();
        app.UseAuthorization();
        return app;
    }
}
