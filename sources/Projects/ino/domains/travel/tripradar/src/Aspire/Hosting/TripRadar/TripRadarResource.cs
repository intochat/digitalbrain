namespace Aspire.Hosting.TripRadar;

internal sealed class TripRadarResource(IDistributedApplicationBuilder builder, string name) : Resource(name)
{
    public IDistributedApplicationBuilder Builder { get; } = builder;

    public IResourceBuilder<KafkaServerResource>? Kafka { get; set; }

    public IResourceBuilder<PostgresServerResource>? Postgres { get; set; }

    public IResourceBuilder<PostgresDatabaseResource>? Database { get; set; }

    public IResourceBuilder<RedisResource>? Redis { get; set; }

    public IResourceBuilder<ElasticsearchResource>? Elasticsearch { get; set; }

    public IResourceBuilder<ContainerResource>? Kibana { get; set; }

    public IResourceBuilder<FlagdResource>? Flagd { get; set; }

    public IResourceBuilder<StripeResource>? Stripe { get; set; }

    public IResourceBuilder<ProjectResource>? Migrations { get; set; }

    public IResourceBuilder<ProjectResource> Api { get; set; } = default!;

    public IResourceBuilder<ProjectResource> Jobs { get; set; } = default!;

    public IResourceBuilder<ExecutableResource>? WebUI { get; set; }

    public IResourceBuilder<ParameterResource> ApiKey { get; set; } = default!;

    public IResourceBuilder<ParameterResource> StripePublishableKey { get; set; } = default!;

    public IResourceBuilder<ParameterResource> SiloGraphQlBearerToken { get; set; } = default!;

    public IResourceBuilder<ParameterResource> InternalApiKey { get; set; } = default!;

    public TripRadarServices ToServices() =>
        new(
            Api,
            Jobs,
            Migrations ?? throw new InvalidOperationException("TripRadar migrations resource is not configured."),
            Postgres ?? throw new InvalidOperationException("TripRadar postgres resource is not configured."),
            Database ?? throw new InvalidOperationException("TripRadar database resource is not configured."),
            Redis ?? throw new InvalidOperationException("TripRadar redis resource is not configured."),
            Flagd ?? throw new InvalidOperationException("TripRadar flagd resource is not configured."),
            Stripe ?? throw new InvalidOperationException("TripRadar stripe resource is not configured."),
            ApiKey,
            StripePublishableKey,
            SiloGraphQlBearerToken,
            InternalApiKey);
}
