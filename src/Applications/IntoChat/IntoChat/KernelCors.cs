using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace IntoChat;

// The shell may have a separate web origin. An explicitly configured loopback origin
// also admits its localhost/127.0.0.1 aliases at the same port for local browser hosting.
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
            if (ResolveOrigins(options.Value) is { Length: > 0 } origins)
            {
                cors.AddPolicy(PolicyName, policy => policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
            }
        });

        return builder;
    }

    public static WebApplication UseKernelCors(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (ResolveOrigins(app.Services.GetRequiredService<IOptions<KernelCorsOptions>>().Value).Length > 0)
        {
            // Ahead of the auth gate so a 401 still carries the CORS headers the
            // browser needs to surface it as a status rather than a network error.
            app.UseCors(PolicyName);
        }

        return app;
    }

    private static string[] ResolveOrigins(KernelCorsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AllowedOrigin))
        {
            return [];
        }

        var origin = options.AllowedOrigin.Trim().TrimEnd('/');
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https"))
        {
            return [origin];
        }

        return new[]
        {
            origin,
            new UriBuilder(uri) { Host = "localhost" }.Uri.GetLeftPart(UriPartial.Authority),
            new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.GetLeftPart(UriPartial.Authority),
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}