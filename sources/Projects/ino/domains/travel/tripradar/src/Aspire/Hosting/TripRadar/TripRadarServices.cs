namespace Aspire.Hosting.TripRadar;

internal record TripRadarServices(
    IResourceBuilder<ProjectResource> Api,
    IResourceBuilder<ProjectResource> Jobs,
    IResourceBuilder<ProjectResource> Migrations,
    IResourceBuilder<PostgresServerResource> Postgres,
    IResourceBuilder<PostgresDatabaseResource> Database,
    IResourceBuilder<RedisResource> Redis,
    IResourceBuilder<FlagdResource> Flagd,
    IResourceBuilder<StripeResource> Stripe,
    IResourceBuilder<ParameterResource> ApiKey,
    IResourceBuilder<ParameterResource> StripePublishableKey,
    IResourceBuilder<ParameterResource> GraphQlBearerToken,
    IResourceBuilder<ParameterResource> InternalApiKey);