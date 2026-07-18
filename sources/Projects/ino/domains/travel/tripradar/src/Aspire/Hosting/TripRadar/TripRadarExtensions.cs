using Aspire.Hosting.TripRadar.Constants;

namespace Aspire.Hosting.TripRadar
{
    internal static class TripRadarExtensions
    {
        #region AppHost Extension
        extension(IDistributedApplicationBuilder builder)
        {
            public TripRadarResource AddTripRadar(
                IResourceBuilder<KafkaServerResource> kafka,
                string name = TripRadarNames.Default,
                Action<TripRadarOptions>? configure = null)
            {
                var options = new TripRadarOptions();
                configure?.Invoke(options);
                string environmentName = options.EnvironmentName;

                var tripRadarResource = new TripRadarResource(builder, name);

                IResourceBuilder<ParameterResource> AddDefaultParameter(string parameterName, string defaultValue) =>
                    builder.AddParameter(parameterName, defaultValue, publishValueAsDefault: true);

                IResourceBuilder<ParameterResource> AddResolvedDefaultParameter(string parameterName, string environmentVariableName, string fallback = "") =>
                    builder.AddParameter(parameterName, () => Resolve(builder, environmentVariableName, fallback), publishValueAsDefault: true);

                IResourceBuilder<ParameterResource> AddResolvedSecretParameter(string parameterName, string environmentVariableName, string fallback = "") =>
                    builder.AddParameter(parameterName, () => Resolve(builder, environmentVariableName, fallback), secret: true);

                IResourceBuilder<ParameterResource> AddSecretParameter(string parameterName) =>
                    builder.AddParameter(parameterName, secret: true);

                IResourceBuilder<ParameterResource> AddDevSecret(string parameterName, string defaultValue) =>
                    builder.AddParameter(parameterName, () => ResolveParameter(builder, parameterName, defaultValue), secret: true);

                // Internal dev secrets — stable hardcoded defaults, overridable via Parameters:* in user-secrets
                var jwtSecret = AddDevSecret(TripRadarConstants.ParameterNames.JwtSecret, TripRadarConstants.ParameterDefaults.JwtSecret);
                var jwtRefreshTokenSecret = AddDevSecret(TripRadarConstants.ParameterNames.JwtRefreshTokenSecret, TripRadarConstants.ParameterDefaults.JwtRefreshTokenSecret);
                var encryptionKey = AddDevSecret(TripRadarConstants.ParameterNames.EncryptionKey, TripRadarConstants.ParameterDefaults.EncryptionKey);
                var apiKey = AddDevSecret(TripRadarConstants.ParameterNames.ApiKey, TripRadarConstants.ParameterDefaults.ApiKey);
                var siloGraphQlBearerToken = AddDevSecret(TripRadarConstants.ParameterNames.SiloGraphQlBearerToken, TripRadarConstants.ParameterDefaults.GraphQlBearerToken);
                var internalApiKey = AddDevSecret(TripRadarConstants.ParameterNames.InternalApiKey, TripRadarConstants.ParameterDefaults.InternalApiKey);
                var appSecret = AddDevSecret(TripRadarConstants.ParameterNames.AppSecret, TripRadarConstants.ParameterDefaults.AppSecret);
                var hangfireAdminPassword = AddDevSecret(TripRadarConstants.ParameterNames.HangfireAdminPassword, TripRadarConstants.ParameterDefaults.HangfireAdminPassword);
                var postgresPassword = AddDevSecret(TripRadarConstants.ParameterNames.PostgresPassword, TripRadarConstants.ParameterDefaults.PostgresPassword)
                    .WithDescription("PostgreSQL password. Set manually for stable access from external DB tools (JetBrains, pgAdmin).", enableMarkdown: true);

                // External service secrets — Aspire prompts on first run, values persist in ~/.aspire/secrets.json
                var stripeSecretKey = AddSecretParameter(TripRadarConstants.ParameterNames.StripeSecretKey)
                    .WithDescription("Stripe secret key. Get from [Stripe Dashboard](https://dashboard.stripe.com/apikeys).", enableMarkdown: true);
                var stripePublishableKey = AddSecretParameter(TripRadarConstants.ParameterNames.StripePublishableKey)
                    .WithDescription("Stripe publishable key. Get from [Stripe Dashboard](https://dashboard.stripe.com/apikeys).", enableMarkdown: true);
                var googleClientId = AddSecretParameter(TripRadarConstants.ParameterNames.GoogleClientId)
                    .WithDescription("Google OAuth Client ID. Get from [Google Cloud Console](https://console.cloud.google.com/apis/credentials).", enableMarkdown: true);
                var googleClientSecret = AddSecretParameter(TripRadarConstants.ParameterNames.GoogleClientSecret)
                    .WithDescription("Google OAuth Client Secret. Same credentials page as Client ID.", enableMarkdown: true);
                var telegramClientId = AddResolvedDefaultParameter(TripRadarConstants.ParameterNames.TelegramClientId, TripRadarConstants.EnvironmentVariables.TelegramClientId);
                var telegramClientSecret = AddResolvedSecretParameter(TripRadarConstants.ParameterNames.TelegramClientSecret, TripRadarConstants.EnvironmentVariables.TelegramClientSecret);
                var serpApiKey = AddSecretParameter(TripRadarConstants.ParameterNames.SerpApiKey)
                    .WithDescription("SerpApi key for flight/hotel search. Get from [SerpApi Dashboard](https://serpapi.com/dashboard).", enableMarkdown: true);
                var emailConnectionString = AddSecretParameter(TripRadarConstants.ParameterNames.EmailConnectionString)
                    .WithDescription("Email service connection string (e.g. Azure Communication Services).", enableMarkdown: true);

                // Dev defaults — sensible values, editable in dashboard
                var corsOrigins = AddDefaultParameter(TripRadarConstants.ParameterNames.CorsOrigins, TripRadarConstants.ParameterDefaults.CorsOrigins);
                var stripeAllowUnverifiedWebhooksInDevelopment = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeAllowUnverifiedWebhooksInDevelopment, TripRadarConstants.ParameterDefaults.StripeAllowUnverifiedWebhooksInDevelopment);
                var stripeSuccessUrl = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeSuccessUrl, TripRadarConstants.ParameterDefaults.StripeSuccessUrl);
                var stripeCancelUrl = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeCancelUrl, TripRadarConstants.ParameterDefaults.StripeCancelUrl);
                var stripeBasicTierPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeBasicTierPriceId, TripRadarConstants.ParameterDefaults.StripeBasicTierPriceId);
                var stripeEssentialTierPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeEssentialTierPriceId, TripRadarConstants.ParameterDefaults.StripeEssentialTierPriceId);
                var stripeAdvancedTierPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeAdvancedTierPriceId, TripRadarConstants.ParameterDefaults.StripeAdvancedTierPriceId);
                var stripeBasicTierYearlyPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeBasicTierYearlyPriceId, TripRadarConstants.ParameterDefaults.StripeBasicTierYearlyPriceId);
                var stripeEssentialTierYearlyPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeEssentialTierYearlyPriceId, TripRadarConstants.ParameterDefaults.StripeEssentialTierYearlyPriceId);
                var stripeAdvancedTierYearlyPriceId = AddDefaultParameter(TripRadarConstants.ParameterNames.StripeAdvancedTierYearlyPriceId, TripRadarConstants.ParameterDefaults.StripeAdvancedTierYearlyPriceId);
                var emailSenderName = AddDefaultParameter(TripRadarConstants.ParameterNames.EmailSenderName, TripRadarConstants.ParameterDefaults.EmailSenderName);
                var emailSenderEmail = AddDefaultParameter(TripRadarConstants.ParameterNames.EmailSenderEmail, TripRadarConstants.ParameterDefaults.EmailSenderEmail);
                var emailApiBaseUrl = AddDefaultParameter(TripRadarConstants.ParameterNames.EmailApiBaseUrl, TripRadarConstants.ParameterDefaults.EmailApiBaseUrl);
                var redirectUrl = AddDefaultParameter(TripRadarConstants.ParameterNames.RedirectUrl, TripRadarConstants.ParameterDefaults.RedirectUrl);

                var blobStorageUrl = AddSecretParameter(TripRadarConstants.ParameterNames.BlobStorageUrl)
                    .WithDescription("Azure Blob Storage URL for email assets.", enableMarkdown: true);
                var blobStorageSasToken = AddSecretParameter(TripRadarConstants.ParameterNames.BlobStorageSasToken)
                    .WithDescription("Azure Blob Storage SAS token for email assets.", enableMarkdown: true);
                var emailLogoUrl = AddResolvedDefaultParameter(
                    TripRadarConstants.ParameterNames.EmailLogoUrl,
                    TripRadarConstants.EnvironmentVariables.EmailLogoUrl,
                    TripRadarConstants.ParameterDefaults.EmailLogoUrl);
                var hangfireAdminUsername = AddDefaultParameter(TripRadarConstants.ParameterNames.HangfireAdminUsername, TripRadarConstants.ParameterDefaults.HangfireAdminUsername);

                // Reuse the telegram-bot-token registered by Telegram extension
                var telegramBotToken = builder.Resources.OfType<ParameterResource>()
                    .FirstOrDefault(r => r.Name == TripRadarConstants.ParameterNames.TelegramBotToken) is { } existing
                    ? builder.CreateResourceBuilder(existing)
                    : builder.AddParameter(TripRadarConstants.ParameterNames.TelegramBotToken, secret: true)
                        .WithDescription("Telegram bot token. Get from [@BotFather](https://t.me/BotFather).", enableMarkdown: true);

                var postgres = builder
                    .AddPostgres(TripRadarNames.Postgres)
                    .WithPassword(postgresPassword)
                    .WithHostPort(5433)

                    .WithDataVolume(TripRadarNames.PostgresData)
                    .WithLifetime(ContainerLifetime.Persistent)
                    .WithPgAdmin(container => container.WithLifetime(ContainerLifetime.Persistent));

                var db = postgres
                    .AddDatabase(TripRadarNames.Database);

                var redis = builder
                    .AddRedis(TripRadarNames.Redis)

                    .WithDataVolume(TripRadarNames.RedisData)
                    .WithLifetime(ContainerLifetime.Persistent)
                    .WithRedisInsight(insight => insight.WithLifetime(ContainerLifetime.Persistent));

                IResourceBuilder<ElasticsearchResource>? elasticsearch = null;
                IResourceBuilder<ContainerResource>? kibana = null;
                if (!options.SkipElasticsearch)
                {
                    elasticsearch = builder
                        .AddElasticsearch(TripRadarNames.Elasticsearch)
    
                        .WithDataVolume(TripRadarNames.ElasticsearchData)
                        .WithLifetime(ContainerLifetime.Persistent);

                    kibana = builder
                        .AddContainer(TripRadarNames.Kibana, TripRadarConstants.ContainerImages.Kibana, TripRadarConstants.ContainerImageTags.Kibana)
    
                        .WithReference(elasticsearch)
                        .WithEndpoint(
                            port: TripRadarConstants.Ports.Kibana,
                            targetPort: TripRadarConstants.Ports.Kibana,
                            scheme: TripRadarConstants.Endpoints.Http,
                            name: TripRadarConstants.Endpoints.KibanaHttp)
                        .WithLifetime(ContainerLifetime.Persistent);
                }

                var flagd = builder
                    .AddFlagd(TripRadarNames.Flagd)

                    .WithBindFileSync(TripRadarConstants.Paths.FlagsDirectory)
                    .WithLifetime(ContainerLifetime.Persistent);

                var migrations = builder
                    .AddProject<Projects.TripRadar_Server_Db>(TripRadarNames.Migrations)

                    .WithReference(db)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.AspNetCoreEnvironment, environmentName)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.DotNetEnvironment, environmentName)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarDbAllowSchemaResetOnRelationExists, TripRadarConstants.ConfigurationValues.False)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.EncryptionUserDataKey, encryptionKey)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesBasicTierPriceId, stripeBasicTierPriceId)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesEssentialTierPriceId, stripeEssentialTierPriceId)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesAdvancedTierPriceId, stripeAdvancedTierPriceId)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesBasicTierYearlyPriceId, stripeBasicTierYearlyPriceId)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesEssentialTierYearlyPriceId, stripeEssentialTierYearlyPriceId)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesAdvancedTierYearlyPriceId, stripeAdvancedTierYearlyPriceId)
                    .WaitFor(postgres);

                IResourceBuilder<ProjectResource> api = AddApiService();
                IResourceBuilder<ProjectResource> jobs = AddJobsService(api);

                var stripeWebhookEndpoint = builder.AddExternalService(
                    $"{TripRadarNames.Stripe}-webhook-endpoint",
                    $"http://host.docker.internal:{TripRadarConstants.Ports.Api}");

                var stripe = builder.AddStripe(TripRadarNames.Stripe, stripeSecretKey)

                    .WithListen(stripeWebhookEndpoint, TripRadarConstants.ConfigurationValues.StripeWebhookPath.TrimStart('/'));

                api.WithReference(stripe, TripRadarConstants.ConfigurationKeys.PaymentSettingsStripeWebhookSecret);

                if (options.MockExternalApis)
                {
                    ConfigureMockApis(api);
                    ConfigureMockApis(jobs);
                }

                tripRadarResource.Kafka = kafka;
                tripRadarResource.Postgres = postgres;
                tripRadarResource.Database = db;
                tripRadarResource.Redis = redis;
                tripRadarResource.Elasticsearch = elasticsearch;
                tripRadarResource.Kibana = kibana;
                tripRadarResource.Flagd = flagd;
                tripRadarResource.Stripe = stripe;
                tripRadarResource.Migrations = migrations;
                tripRadarResource.Api = api;
                tripRadarResource.Jobs = jobs;
                tripRadarResource.ApiKey = apiKey;
                tripRadarResource.StripePublishableKey = stripePublishableKey;
                tripRadarResource.SiloGraphQlBearerToken = siloGraphQlBearerToken;
                tripRadarResource.InternalApiKey = internalApiKey;

                return tripRadarResource;

                IResourceBuilder<ProjectResource> AddApiService()
                {
                    IResourceBuilder<ProjectResource> addApiService = builder
                        .AddProject<Projects.TripRadar_Server_API>(TripRadarNames.Api, projectOptions => projectOptions.ExcludeLaunchProfile = true)
    
                        .WithHttpEndpoint(port: TripRadarConstants.Ports.Api, name: TripRadarConstants.Endpoints.Http)
                        .WithHttpHealthCheck(TripRadarConstants.Routes.Health, endpointName: TripRadarConstants.Endpoints.Http);

                    addApiService = ConfigureEndpointLinks(
                        addApiService,
                        TripRadarConstants.Endpoints.Http,
                        TripRadarConstants.DisplayTexts.Api,
                        (TripRadarConstants.Routes.Scalar, TripRadarConstants.DisplayTexts.Scalar),
                        (TripRadarConstants.Routes.GraphQl, TripRadarConstants.DisplayTexts.GraphQl),
                        (TripRadarConstants.Routes.Health, TripRadarConstants.DisplayTexts.Health));

                    addApiService = ConfigureSharedServiceReferences(addApiService);
                    addApiService = ConfigureSharedServiceEnvironment(addApiService);
                    addApiService = ConfigureApiEnvironment(addApiService);
                    addApiService = ConfigureSharedServiceWaits(addApiService);
                    ConfigureElasticsearch(addApiService);

                    return addApiService;
                }

                IResourceBuilder<ProjectResource> AddJobsService(IResourceBuilder<ProjectResource> resourceBuilder)
                {
                    IResourceBuilder<ProjectResource> configureJobsEnvironment = builder
                        .AddProject<Projects.TripRadar_Server_Jobs_API>(
                            TripRadarNames.Jobs,
                            projectOptions => projectOptions.ExcludeLaunchProfile = true)
    
                        .WithHttpEndpoint(port: TripRadarConstants.Ports.Jobs, name: TripRadarConstants.Endpoints.Http)
                        .WithHttpHealthCheck(TripRadarConstants.Routes.Health, endpointName: TripRadarConstants.Endpoints.Http);

                    configureJobsEnvironment = ConfigureEndpointLinks(
                        configureJobsEnvironment,
                        TripRadarConstants.Endpoints.Http,
                        TripRadarConstants.DisplayTexts.JobsApi,
                        (TripRadarConstants.Routes.Hangfire, TripRadarConstants.DisplayTexts.Hangfire),
                        (TripRadarConstants.Routes.Health, TripRadarConstants.DisplayTexts.Health));

                    configureJobsEnvironment = configureJobsEnvironment.WithReference(resourceBuilder);
                    configureJobsEnvironment = ConfigureSharedServiceReferences(configureJobsEnvironment);
                    configureJobsEnvironment = ConfigureSharedServiceEnvironment(configureJobsEnvironment);
                    configureJobsEnvironment = ConfigureJobsEnvironment(configureJobsEnvironment);
                    configureJobsEnvironment = ConfigureSharedServiceWaits(configureJobsEnvironment);
                    ConfigureElasticsearch(configureJobsEnvironment);

                    return configureJobsEnvironment;
                }

                IResourceBuilder<T> ConfigureSharedServiceReferences<T>(IResourceBuilder<T> serviceBuilder)
                    where T : IResourceWithEnvironment =>
                    serviceBuilder
                        .WithReference(db)
                        .WithReference(redis)
                        .WithReference(flagd)
                        .WithReference(kafka, connectionName: TripRadarConstants.ConnectionNames.Kafka);

                IResourceBuilder<T> ConfigureSharedServiceWaits<T>(IResourceBuilder<T> serviceBuilder) where T : IResourceWithWaitSupport =>
                    serviceBuilder
                        .WaitFor(postgres)
                        .WaitFor(redis)
                        .WaitFor(flagd)
                        .WaitForCompletion(migrations);

                IResourceBuilder<T> ConfigureSharedServiceEnvironment<T>(IResourceBuilder<T> serviceBuilder) where T : IResourceWithEnvironment =>
                    serviceBuilder
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.AspNetCoreEnvironment, environmentName)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.DotNetEnvironment, environmentName)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JwtKey, jwtSecret)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JwtRefreshTokenKey, jwtRefreshTokenSecret)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JwtIssuer, TripRadarConstants.ConfigurationValues.JwtIssuerTripRadar)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JwtAudience, TripRadarConstants.ConfigurationValues.JwtAudienceTripRadarUsers)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JwtDurationInMinutes, TripRadarConstants.ConfigurationValues.JwtDurationInMinutes)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EncryptionUserDataKey, encryptionKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.ApiKey, apiKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.InternalApiKey, internalApiKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailSenderEmail, emailSenderEmail)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailSenderName, emailSenderName)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailConnectionString, emailConnectionString)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailBaseUrl, emailApiBaseUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailRedirectUrl, redirectUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailLogoUrl, emailLogoUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailBlobStorageUrl, blobStorageUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.EmailBlobStorageSasToken, blobStorageSasToken)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.KafkaBootstrapServers, kafka.Resource.ConnectionStringExpression)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.KafkaSecurityProtocol, TripRadarConstants.ConfigurationValues.KafkaSecurityProtocolPlaintext)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.KafkaSaslMechanism, string.Empty)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.KafkaSaslUsername, string.Empty)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.KafkaSaslPassword, string.Empty);

                IResourceBuilder<T> ConfigureApiEnvironment<T>(IResourceBuilder<T> serviceBuilder) where T : IResourceWithEnvironment =>
                    serviceBuilder
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.CorsOriginsWhiteList, corsOrigins)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.GoogleAuthClientId, googleClientId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.GoogleAuthClientSecret, googleClientSecret)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.SerpApiSettingsApiKey, serpApiKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripeSecretKey, stripeSecretKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePublishableKey, stripePublishableKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripeAllowUnverifiedWebhooksInDevelopment, stripeAllowUnverifiedWebhooksInDevelopment)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripeSuccessUrl, stripeSuccessUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripeCancelUrl, stripeCancelUrl)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesBasicTierPriceId, stripeBasicTierPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesEssentialTierPriceId, stripeEssentialTierPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesAdvancedTierPriceId, stripeAdvancedTierPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesBasicTierYearlyPriceId, stripeBasicTierYearlyPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesEssentialTierYearlyPriceId, stripeEssentialTierYearlyPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.PaymentSettingsStripePricesAdvancedTierYearlyPriceId, stripeAdvancedTierYearlyPriceId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.AppSecret, appSecret)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.TelegramSettingsBotToken, telegramBotToken)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.TelegramSettingsClientId, telegramClientId)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.TelegramSettingsClientSecret, telegramClientSecret)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.DisableHttpsRedirection, TripRadarConstants.ConfigurationValues.True);

                IResourceBuilder<T> ConfigureJobsEnvironment<T>(IResourceBuilder<T> serviceBuilder) where T : IResourceWithEnvironment =>
                    serviceBuilder
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.SerpApiSettingsApiKey, serpApiKey)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.HangfireDashboardAuthorizationUser0Username, hangfireAdminUsername)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.HangfireDashboardAuthorizationUser0Password, hangfireAdminPassword)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.HangfireIsFullAccessModeEnabled, TripRadarConstants.ConfigurationValues.True)
                        .WithEnvironment(TripRadarConstants.ConfigurationKeys.JobSettingsMetterBillingJobStaleProcessingMaxAgeMinutes, TripRadarConstants.ConfigurationValues.JobSettingsMetterBillingJobStaleProcessingMaxAgeMinutes);

                void ConfigureElasticsearch<T>(IResourceBuilder<T> serviceBuilder) where T : IResourceWithEnvironment
                {
                    if (options.SkipElasticsearch || elasticsearch is null) return;

                    serviceBuilder.WithReference(elasticsearch)
                        .WithEnvironment(
                            TripRadarConstants.ConfigurationKeys.ElasticConfigurationUri,
                            elasticsearch.GetEndpoint(TripRadarConstants.Endpoints.Http));
                }
            }
        }
        #endregion

        #region TripRadar Resource Extension
        extension(TripRadarResource tripRadar)
        {
            public TripRadarResource WithRealApis() => tripRadar;
        }
        #endregion

        #region Generic Resource Extensions
        extension<T>(IResourceBuilder<T> builder) where T : IResourceWithEnvironment, IResourceWithWaitSupport
        {
            public IResourceBuilder<T> WithReference(TripRadarResource tripRadar)
            {
                ArgumentNullException.ThrowIfNull(tripRadar);
                var kafka = tripRadar.Kafka ?? throw new InvalidOperationException("TripRadar Kafka resource is not configured.");

                return builder
                    .WithReference(tripRadar.Api)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiApiKey, tripRadar.ApiKey)
                    .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiBearerToken, tripRadar.SiloGraphQlBearerToken)
                    .WithReference(kafka, connectionName: TripRadarConstants.ConnectionNames.Kafka);
            }
        }

        extension<T>(IResourceBuilder<T> builder) where T : IResourceWithWaitSupport
        {
            public IResourceBuilder<T> WaitFor(TripRadarResource tripRadar)
            {
                ArgumentNullException.ThrowIfNull(tripRadar);
                var kafka = tripRadar.Kafka ?? throw new InvalidOperationException("TripRadar Kafka resource is not configured.");

                return builder
                    .WaitFor(tripRadar.Api)
                    .WaitFor(kafka);
            }
        }
        #endregion

        #region Internal Helpers
        private static void ConfigureMockApis<T>(IResourceBuilder<T> builder) where T : IResourceWithEnvironment =>
            builder
                .WithEnvironment(TripRadarConstants.ConfigurationKeys.MockApiSerpApi, TripRadarConstants.ConfigurationValues.True);

        private static IResourceBuilder<T> ConfigureEndpointLinks<T>(
            IResourceBuilder<T> serviceBuilder,
            string endpointName,
            string endpointDisplayText,
            params (string Url, string DisplayText)[] links)
            where T : IResourceWithEndpoints
        {
            IResourceBuilder<T> configuredBuilder = serviceBuilder
                .WithUrlForEndpoint(endpointName, endpoint => endpoint.DisplayText = endpointDisplayText);

            foreach ((string url, string displayText) in links)
            {
                configuredBuilder = configuredBuilder.WithUrlForEndpoint(
                    endpointName,
                    _ => new ResourceUrlAnnotation { Url = url, DisplayText = displayText });
            }

            return configuredBuilder;
        }

        internal static string Resolve(IDistributedApplicationBuilder builder, string key, string fallback = "")
        {
            var configuredValue = builder.Configuration[key];
            if (!string.IsNullOrWhiteSpace(configuredValue))
            {
                return configuredValue;
            }

            var environmentValue = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(environmentValue) ? fallback : environmentValue;
        }

        internal static string ResolveParameter(IDistributedApplicationBuilder builder, string parameterName, string fallback = "")
        {
            var configuredParameterValue = builder.Configuration[$"Parameters:{parameterName}"];
            return !string.IsNullOrWhiteSpace(configuredParameterValue)
                ? configuredParameterValue
                : fallback;
        }

        
        internal static string ResolveTelegramAuthBaseUrl(IDistributedApplicationBuilder builder)
        {
            var explicitAuthBaseUrl = Resolve(builder, TripRadarConstants.EnvironmentVariables.TelegramAuthBaseUrl);
            if (!string.IsNullOrWhiteSpace(explicitAuthBaseUrl))
                return explicitAuthBaseUrl;

            var miniAppUrl = Resolve(builder, TripRadarConstants.EnvironmentVariables.TelegramMiniAppUrl);
            var baseUrlFromMiniApp = TryGetUriAuthority(miniAppUrl);
            if (!string.IsNullOrWhiteSpace(baseUrlFromMiniApp))
                return baseUrlFromMiniApp;

            var webhookUrl = Resolve(builder, TripRadarConstants.EnvironmentVariables.TelegramWebhookUrl);
            var baseUrlFromWebhook = TryGetUriAuthority(webhookUrl);
            if (!string.IsNullOrWhiteSpace(baseUrlFromWebhook))
                return baseUrlFromWebhook;

            return TripRadarConstants.WebUi.DefaultAuthBaseUrl;
        }

        private static string TryGetUriAuthority(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return string.Empty;

            return uri.GetLeftPart(UriPartial.Authority);
        }
        #endregion
    }
}
