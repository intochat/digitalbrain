using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using TripRadar.Server.API.Middlewares;
using TripRadar.Server.API.Security;

namespace TripRadar.Server.API.Extensions;

internal static class ApplicationBuilderExtensions
{
    extension(WebApplication app)
    {
        public void BuildApplication()
        {
            app.UseForwardedHeaders();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler();
                app.UseHsts();
            }

            if (!app.Configuration.GetValue<bool>("DisableHttpsRedirection"))
            {
                app.UseHttpsRedirection();
            }

            app.UseResponseCompression();

            var legacyHealthPath = app.Configuration.GetValue<string?>("HealthChecks:Path") ?? "/health";
            var readinessPath = app.Configuration.GetValue<string?>("HealthChecks:ReadinessPath") ?? "/health/ready";
            var livenessPath = app.Configuration.GetValue<string?>("HealthChecks:LivenessPath") ?? "/health/live";

            app.UseHealthChecks(livenessPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

            app.UseHealthChecks(readinessPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("readiness"),
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

            app.UseHealthChecks(legacyHealthPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("readiness"),
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

            app.AddMiddlewares();
            app.UseStatusCodePages();

            app.UseIpRateLimiting();
            app.UseRouting();
            app.UseCors("AllowedOrigins");

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
                app.MapScalarApiReference(options => options
                    .AddPreferredSecuritySchemes(
                        JwtBearerDefaults.AuthenticationScheme,
                        ApiKeyAuthenticationHandler.ApiKeyHeaderName));
            }

            app.UseAuthentication();
            app.UseMiddleware<AntiforgeryCookieMiddleware>();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGraphQL().RequireAuthorization("GraphQLAuth");

            app.UseInDevelopmentEnv();
        }

        private void UseInDevelopmentEnv()
        {
            if (!app.Environment.IsDevelopment())
            {
                return;
            }

            app.UseMigrationsEndPoint();
            app.UseDeveloperExceptionPage();
            app.MapDevelopmentAuthEndpoints();
        }

        private void AddMiddlewares()
        {
            app.UseMiddleware<SecurityHeadersMiddleware>();
            app.UseMiddleware<ContentSecurityPolicyMiddleware>();
            app.UseMiddleware<RateLimitHeaderSanitizerMiddleware>();
        }
    }
}
