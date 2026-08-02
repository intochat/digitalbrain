using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Behaviors;

public static class BehaviorBrokerAuth
{
    public const string CredentialConfigurationKey = BehaviorBrokerContract.CredentialConfigurationKey;
    public const string CredentialHeaderName = BehaviorBrokerContract.CredentialHeaderName;

    public static IServiceCollection AddBehaviorBrokerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var credential = configuration[CredentialConfigurationKey];
        if (string.IsNullOrWhiteSpace(credential) && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Configuration '{CredentialConfigurationKey}' is required outside the Development environment " +
                $"(current environment: '{environment.EnvironmentName}'); without it every request to the behavior " +
                "broker rail would 401. Set it, or run with the Development environment if the broker rail is " +
                "genuinely disabled.");
        }

        services.TryAddSingleton(new BehaviorBrokerCredentialGate(credential));
        return services;
    }

    public static IApplicationBuilder UseBehaviorBrokerAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/v1/behaviors/broker", StringComparison.Ordinal))
            {
                await next().ConfigureAwait(false);
                return;
            }

            var gate = context.RequestServices.GetRequiredService<BehaviorBrokerCredentialGate>();
            if (!gate.IsAuthorized(context.Request.Headers[CredentialHeaderName].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync("unauthorized", context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await next().ConfigureAwait(false);
        });
    }
}

internal sealed class BehaviorBrokerCredentialGate
{
    private readonly byte[]? expectedUtf8;

    public BehaviorBrokerCredentialGate(string? configuredCredential)
    {
        if (string.IsNullOrWhiteSpace(configuredCredential))
        {
            expectedUtf8 = null;
            return;
        }

        expectedUtf8 = Encoding.UTF8.GetBytes(configuredCredential);
    }

    public bool IsConfigured => expectedUtf8 is { Length: > 0 };

    public bool IsAuthorized(string? presentedCredential)
    {
        if (expectedUtf8 is not { Length: > 0 })
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(presentedCredential))
        {
            CryptographicOperations.FixedTimeEquals(expectedUtf8, expectedUtf8);
            return false;
        }

        var presented = Encoding.UTF8.GetBytes(presentedCredential);
        try
        {
            if (presented.Length != expectedUtf8.Length)
            {
                CryptographicOperations.FixedTimeEquals(expectedUtf8, expectedUtf8);
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedUtf8, presented);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(presented);
        }
    }
}
