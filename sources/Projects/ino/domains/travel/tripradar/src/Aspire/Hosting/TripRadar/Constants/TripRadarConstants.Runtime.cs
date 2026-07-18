namespace Aspire.Hosting.TripRadar.Constants;

internal static partial class TripRadarConstants
{
    internal static class Endpoints
    {
        public const string KibanaHttp = "kibana-http";
        public const string Http = "http";
        public const string Https = "https";
    }

    internal static class Ports
    {
        public const int Api = 5330;
        public const int Jobs = 5382;
        public const int Kibana = 5601;
    }

    internal static class Routes
    {
        public const string Health = "/health";
        public const string Scalar = "/scalar/v1";
        public const string GraphQl = "/graphql";
        public const string Hangfire = "/hangfire";
    }

    internal static class DisplayTexts
    {
        public const string Api = "API";
        public const string JobsApi = "Jobs API";
        public const string Health = "Health";
        public const string Scalar = "Scalar";
        public const string GraphQl = "GraphQL";
        public const string Hangfire = "Hangfire";
    }

    internal static class ConnectionNames
    {
        public const string Kafka = "kafka";
    }

    internal static class Bot
    {
        public const int Port = 5160;
        public const string BotToken = "Bot__BotToken";
        public const string SessionSyncSecret = "Bot__SessionSyncSecret";
        public const string SessionSyncSecretEnvVar = "TELEGRAM_BOTFLOW_SESSION_SYNC_SECRET";
        public const string InternalApiKey = "Bot__InternalApiKey";
        public const string WebsiteUrl = "Bot__WebsiteUrl";
        public const string WebsiteTunnelName = "cloudflared-website";
    }

    internal static class Paths
    {
        public const string FlagsDirectory = "./Hosting/TripRadar/flags/";
    }

    internal static class ContainerImages
    {
        public const string Kibana = "docker.elastic.co/kibana/kibana";
    }

    internal static class ContainerImageTags
    {
        public const string Kibana = "9.1.4";
    }
}
