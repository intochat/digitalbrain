using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace IntoChat;

// The deployed shell is served from a different Static Web App origin. Inactive unless
// AllowedOriginConfigurationKey is configured, so same-origin and local hosting are unchanged.
internal static class KernelCors
{
    public const string AllowedOriginConfigurationKey = "DigitalBrain:Cors:AllowedOrigin";

    private const string PolicyName = "shell";

    public static IHostApplicationBuilder AddKernelCors(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<KernelCorsOptions>().BindConfiguration(KernelCorsOptions.SectionName);
        builder.Services.AddCors();
        builder.Services.AddOptions<CorsOptions>().Configure<IOptions<KernelCorsOptions>>((cors, options) =>
        {
            if (ResolveOrigin(options.Value) is { } origin)
            {
                cors.AddPolicy(PolicyName, policy => policy
                    .WithOrigins(origin)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            }
        });

        return builder;
    }

    public static WebApplication UseKernelCors(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ResolveOrigin(app.Services.GetRequiredService<IOptions<KernelCorsOptions>>().Value) is not null)
        {
            // Ahead of the auth gate so a 401 still carries the CORS headers the
            // browser needs to surface it as a status rather than a network error.
            app.UseCors(PolicyName);
        }

        return app;
    }

    private static string? ResolveOrigin(KernelCorsOptions options)
        => options.AllowedOrigin is { Length: > 0 } origin
            ? origin.TrimEnd('/')
            : null;
}
