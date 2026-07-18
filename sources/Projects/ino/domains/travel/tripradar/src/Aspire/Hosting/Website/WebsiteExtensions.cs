using Aspire.Hosting.Cloudflared;
using Aspire.Hosting.Telegram;
using Aspire.Hosting.TripRadar;
using Aspire.Hosting.TripRadar.Constants;

namespace Aspire.Hosting.Website;

internal static class WebsiteExtensions
{
    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<ExecutableResource> AddWebsite()
        {
            var webUiPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, TripRadarConstants.WebUi.ProjectRelativePath));
            var webUiAllowedHosts = builder.AddParameter(TripRadarConstants.WebUi.AllowedHostsParameterName, TripRadarConstants.WebUi.AllowedHostsDefault, publishValueAsDefault: true);
            var firebaseApiKey = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseApiKeyParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseApiKey,
                    TripRadarConstants.WebUi.FirebaseApiKeyDefault),
                secret: true);
            var firebaseAuthDomain = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseAuthDomainParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseAuthDomain,
                    TripRadarConstants.WebUi.FirebaseAuthDomainDefault),
                publishValueAsDefault: true);
            var firebaseProjectId = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseProjectIdParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseProjectId,
                    TripRadarConstants.WebUi.FirebaseProjectIdDefault),
                publishValueAsDefault: true);
            var firebaseStorageBucket = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseStorageBucketParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseStorageBucket,
                    TripRadarConstants.WebUi.FirebaseStorageBucketDefault),
                publishValueAsDefault: true);
            var firebaseMessagingSenderId = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseMessagingSenderIdParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseMessagingSenderId,
                    TripRadarConstants.WebUi.FirebaseMessagingSenderIdDefault),
                publishValueAsDefault: true);
            var firebaseAppId = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseAppIdParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseAppId,
                    TripRadarConstants.WebUi.FirebaseAppIdDefault),
                publishValueAsDefault: true);
            var firebaseMeasurementId = builder.AddParameter(
                TripRadarConstants.WebUi.FirebaseMeasurementIdParameterName,
                () => TripRadarExtensions.Resolve(
                    builder,
                    TripRadarConstants.EnvironmentVariables.FirebaseMeasurementId,
                    TripRadarConstants.WebUi.FirebaseMeasurementIdDefault),
                publishValueAsDefault: true);
            var telemetryEnabled = builder.AddParameter(
                TripRadarConstants.WebUi.TelemetryEnabledParameterName,
                TripRadarConstants.WebUi.TelemetryEnabledDefault,
                publishValueAsDefault: true);
            var frontendErrorIngestUrl = builder.AddParameter(
                TripRadarConstants.WebUi.FrontendErrorIngestUrlParameterName,
                () => TripRadarExtensions.Resolve(builder, TripRadarConstants.EnvironmentVariables.FrontendErrorIngestUrl),
                publishValueAsDefault: true);
            var analyticsDebug = builder.AddParameter(
                TripRadarConstants.WebUi.AnalyticsDebugParameterName,
                TripRadarConstants.WebUi.AnalyticsDebugDefault,
                publishValueAsDefault: true);
            var otelEnabled = builder.AddParameter(
                TripRadarConstants.WebUi.OtelEnabledParameterName,
                TripRadarConstants.WebUi.OtelEnabledDefault,
                publishValueAsDefault: true);
            var otelServiceName = builder.AddParameter(
                TripRadarConstants.WebUi.OtelServiceNameParameterName,
                TripRadarConstants.WebUi.OtelServiceNameDefault,
                publishValueAsDefault: true);
            var otelEndpoint = builder.AddParameter(
                TripRadarConstants.WebUi.OtelEndpointParameterName,
                string.Empty,
                publishValueAsDefault: true);
            var otelHeaders = builder.AddParameter(
                TripRadarConstants.WebUi.OtelHeadersParameterName,
                string.Empty,
                publishValueAsDefault: true);
            var botTokenParam = builder.Resources.OfType<ParameterResource>()
                .First(r => r.Name == TripRadarConstants.ParameterNames.TelegramBotToken);
            var telegramAuthBaseUrl = builder.AddParameter(
                TripRadarConstants.WebUi.TelegramAuthBaseUrlParameterName,
                () => TripRadarExtensions.ResolveTelegramAuthBaseUrl(builder),
                publishValueAsDefault: true);

            var website = builder.AddViteApp(TripRadarNames.Website, webUiPath)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteAllowedHosts, webUiAllowedHosts)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteDevHost, TripRadarConstants.WebUi.DevHost)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteDevPort, TripRadarConstants.WebUi.DevPort)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteDevHttps, TripRadarConstants.ConfigurationValues.True)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseApiKey, firebaseApiKey)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseAuthDomain, firebaseAuthDomain)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseProjectId, firebaseProjectId)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseStorageBucket, firebaseStorageBucket)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseMessagingSenderId, firebaseMessagingSenderId)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseAppId, firebaseAppId)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFirebaseMeasurementId, firebaseMeasurementId)
                .WithEnvironment(async context =>
                {
                    var botToken = await botTokenParam.GetValueAsync(context.CancellationToken);
                    var username = await TelegramBotResolver.ResolveUsernameAsync(botToken, context.CancellationToken);
                    context.EnvironmentVariables[TripRadarConstants.ConfigurationKeys.ViteTelegramBotUsername] = username;
                })
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteTelegramAuthBaseUrl, telegramAuthBaseUrl)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteTelemetryEnabled, telemetryEnabled)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteFrontendErrorIngestUrl, frontendErrorIngestUrl)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteAnalyticsDebug, analyticsDebug)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteOtelEnabled, otelEnabled)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteOtelServiceName, otelServiceName)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteOtelEndpoint, otelEndpoint)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteOtelHeaders, otelHeaders)
                .WithEndpoint(TripRadarConstants.Endpoints.Http, endpoint =>
                {
                    endpoint.IsProxied = false;
                    endpoint.Port = TripRadarConstants.WebUi.EndpointPort;
                    endpoint.TargetPort = TripRadarConstants.WebUi.EndpointPort;
                    endpoint.UriScheme = TripRadarConstants.Endpoints.Https;
                });

            return website.WithCloudflaredTunnel("cloudflared-website", TripRadarConstants.WebUi.EndpointPort, useHttps: true,
                configureEnvironment: (env, tunnelUrl) => env[TripRadarConstants.ConfigurationKeys.ViteTelegramAuthBaseUrl] = tunnelUrl);
        }
    }

    extension(IResourceBuilder<ExecutableResource> website)
    {
        public IResourceBuilder<ExecutableResource> WithReference(TripRadarResource server) =>
            website.WithReference(server.ToServices());

        public IResourceBuilder<ExecutableResource> WithReference(TripRadarServices server) =>
            website
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteApiBaseUrl, server.Api.GetEndpoint(TripRadarConstants.Endpoints.Http))
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteApiKey, server.ApiKey)
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.ViteStripePublishableKey, server.StripePublishableKey)
                .WithReference(server.Api);
    }
}
