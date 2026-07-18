namespace Aspire.Hosting.TripRadar.Constants;

internal static partial class TripRadarConstants
{
    internal static class WebUi
    {
        public const string ProjectRelativePath = "../TripRadar.WebUI";
        public const string AllowedHostsParameterName = "website-allowed-hosts";
        public const string AllowedHostsDefault = ".localhost,.tripradar.io";
        public const string FirebaseApiKeyParameterName = "website-firebase-api-key";
        public const string FirebaseAuthDomainParameterName = "website-firebase-auth-domain";
        public const string FirebaseProjectIdParameterName = "website-firebase-project-id";
        public const string FirebaseStorageBucketParameterName = "website-firebase-storage-bucket";
        public const string FirebaseMessagingSenderIdParameterName = "website-firebase-messaging-sender-id";
        public const string FirebaseAppIdParameterName = "website-firebase-app-id";
        public const string FirebaseMeasurementIdParameterName = "website-firebase-measurement-id";
        public const string FirebaseApiKeyDefault = "AIzaSyCda9g7cIF77DTYdvTnJ5RThRAaALew99Y";
        public const string FirebaseAuthDomainDefault = "trip-radar-466916.firebaseapp.com";
        public const string FirebaseProjectIdDefault = "trip-radar-466916";
        public const string FirebaseStorageBucketDefault = "trip-radar-466916.firebasestorage.app";
        public const string FirebaseMessagingSenderIdDefault = "759163782976";
        public const string FirebaseAppIdDefault = "1:759163782976:web:48fb6e25123205df6edca5";
        public const string FirebaseMeasurementIdDefault = "G-J0M8GT2E1S";
        public const string TelegramAuthBaseUrlParameterName = "website-telegram-auth-base-url";
        public const string TelemetryEnabledParameterName = "website-telemetry-enabled";
        public const string FrontendErrorIngestUrlParameterName = "website-frontend-error-ingest-url";
        public const string AnalyticsDebugParameterName = "website-analytics-debug";
        public const string TelemetryEnabledDefault = "true";
        public const string AnalyticsDebugDefault = "false";
        public const string DevHost = "localhost";
        public const string DevPort = "3000";
        public const int EndpointPort = 3000;
        public const string DefaultAuthBaseUrl = "https://localhost:3000";
        public const string OtelEnabledParameterName = "website-otel-enabled";
        public const string OtelEnabledDefault = "true";
        public const string OtelServiceNameParameterName = "website-otel-service-name";
        public const string OtelServiceNameDefault = "website";
        public const string OtelEndpointParameterName = "website-otel-endpoint";
        public const string OtelHeadersParameterName = "website-otel-headers";
    }
}
