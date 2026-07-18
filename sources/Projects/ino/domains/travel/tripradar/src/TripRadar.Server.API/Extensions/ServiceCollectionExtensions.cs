using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.Routing;
using AspNetCoreRateLimit;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TripRadar.Server.API.Contracts;
using TripRadar.Server.API.Filters;
using TripRadar.Server.API.GraphQL.Queries;
using TripRadar.Server.API.GraphQL.Types;
using TripRadar.Server.API.HealthChecks;
using TripRadar.Server.API.Middlewares;
using TripRadar.Server.API.Security;
using TripRadar.Server.API.Services;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Settings;
using TripRadar.Server.Application.UseCases.SearchEngine.Flights.Queries.GetFlights;
using TripRadar.Server.Comms.Core.Contracts.Exceptions;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Contracts.Authentication;
using TripRadar.Server.Infrastructure.Extensions;
using TripRadar.Server.Infrastructure.Filters;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;
using TripRadar.Server.Infrastructure.Providers.Stripe.Settings;
using TripRadar.Server.Infrastructure.Repositories;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Authentication;
using TripRadar.Server.Infrastructure.Settings;
using Path = System.IO.Path;

namespace TripRadar.Server.API.Extensions;

internal static class ServiceCollectionExtensions
{
    private const string SecurityStampClaimType = "security_stamp";
    private static readonly string[] _localDefaultCorsOrigins =
    [
        "http://localhost:3000",
        "http://localhost:5173",
        "http://127.0.0.1:3000",
        "http://127.0.0.1:5173"
    ];

    extension(IServiceCollection services)
    {
        public IServiceCollection ConfigureApi(IConfiguration configuration, IHostEnvironment environment) =>
            services
                .ConfigureExceptions()
                .ConfigureAutomapper()
                .ConfigureMediator()
                .ConfigureApiVersioning()
                .ConfigureOpenApi()
                .ConfigureHealthChecks()
                .ConfigureCorsPolicy(configuration)
                .ConfigureGraphQl(configuration)
                .ConfigureAntiforgery(environment)
                .ConfigureAuthentication(configuration)
                .ConfigureHangfireClient(configuration)
                .ConfigureServices()
                .ConfigureForwardedHeaders(configuration)
                .ConfigureRateLimiting(configuration)
                .ConfigureSettings(configuration)
                .ConfigureHttps(environment)
                .ConfigureMetricsAndTraces()
                .ConfigureResponseCompression()
                .ConfigureOutputCaching();

        private IServiceCollection ConfigureExceptions() => services.AddProblemDetails().AddSingleton<IExceptionDetails, ValidationFilter>();
        private IServiceCollection ConfigureAutomapper() => services.AddAutoMapper(_ => { }, typeof(ServiceCollectionExtensions));

        private IServiceCollection ConfigureMediator()
        {
            services.AddMediatR(configuration =>
            {
                configuration.RegisterServicesFromAssembly(typeof(ServiceCollectionExtensions).Assembly);
                configuration.RegisterServicesFromAssembly(
                    typeof(GetFlightsQueryHandler).Assembly);
                configuration.RegisterServicesFromAssembly(typeof(AuthenticationService).Assembly);
                configuration.AddOpenBehavior(typeof(LoggingBehavior<,>));
                configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
                configuration.AddOpenBehavior(typeof(MetricBehavior<,>));
                configuration.AutoRegisterRequestProcessors = true;
            });

            services.AddValidatorsFromAssembly(typeof(GetFlightsQueryHandler).Assembly);

            return services;
        }

        private IServiceCollection ConfigureApiVersioning()
        {
            services.AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            });

            services.Configure<RouteOptions>(options =>
            {
                options.ConstraintMap.Add("apiVersion", typeof(ApiVersionRouteConstraint));
            });

            return services;
        }

        private IServiceCollection ConfigureOpenApi()
        {
            services
                .AddOpenApi(options =>
                {
                    options.AddDocumentTransformer((document, _, _) =>
                    {
                        document.Info.Title = "TripRadar API";
                        document.Info.Version = "v1";

                        document.Components ??= new();
                        var bearerScheme = JwtBearerDefaults.AuthenticationScheme;
                        var apiKeyScheme = ApiKeyAuthenticationHandler.ApiKeyHeaderName;

                        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                        {
                            [bearerScheme] = new OpenApiSecurityScheme
                            {
                                Type = SecuritySchemeType.Http,
                                Scheme = "bearer",
                                BearerFormat = "JWT",
                                Description = "JWT token from /api/v1.0/auth/login or /api/v1.0/auth/telegram"
                            },
                            [apiKeyScheme] = new OpenApiSecurityScheme
                            {
                                Type = SecuritySchemeType.ApiKey,
                                In = ParameterLocation.Header,
                                Name = ApiKeyAuthenticationHandler.ApiKeyHeaderName,
                                Description = "API key for all protected endpoints"
                            }
                        };

                        document.Security =
                        [
                            new OpenApiSecurityRequirement
                            {
                                [new OpenApiSecuritySchemeReference(bearerScheme, document)] = [],
                                [new OpenApiSecuritySchemeReference(apiKeyScheme, document)] = []
                            }
                        ];

                        return Task.CompletedTask;
                    });

                    options.AddOperationTransformer((operation, _, _) =>
                    {
                        if (operation.Parameters is { } parameters)
                        {
                            var versionParams = parameters
                                .Where(p => string.Equals(p.Name, "version", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var p in versionParams)
                                parameters.Remove(p);
                        }
                        return Task.CompletedTask;
                    });

                    options.AddDocumentTransformer((document, _, _) =>
                    {
                        var resolvedPaths = new OpenApiPaths();
                        foreach (var (path, item) in document.Paths)
                            resolvedPaths.Add(path.Replace("v{version}", "v1.0"), item);
                        document.Paths = resolvedPaths;
                        return Task.CompletedTask;
                    });
                })
                .AddControllers(options =>
                {
                    options.Filters.Add<InternalAuthFilter>();
                    options.Filters.Add<ApiKeyAuthFilter>();
                    options.Filters.Add<CookieAntiforgeryFilter>();
                })
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                });
            return services;
        }

        private IServiceCollection ConfigureAntiforgery(IHostEnvironment environment)
        {
            services.AddAntiforgery(options =>
            {
                options.HeaderName = AuthCookieHelper.AntiforgeryHeaderName;
                options.Cookie.Name = AuthCookieHelper.AntiforgeryCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("Test") || environment.IsEnvironment("Testing")
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
            });

            return services;
        }

        private IServiceCollection ConfigureHealthChecks()
        {
            // "self" liveness check is registered by AddServiceDefaults()
            services.AddHealthChecks()
                .AddCheck<PostgresReadinessHealthCheck>("postgres", tags: ["readiness"])
                .AddCheck<RedisReadinessHealthCheck>("redis", tags: ["readiness"])
                .AddCheck<KafkaReadinessHealthCheck>("kafka", tags: ["readiness"]);
            return services;
        }

        private IServiceCollection ConfigureCorsPolicy(IConfiguration configuration)
        {
            var origins = ResolveCorsOrigins(configuration["CorsOriginsWhiteList"]);

            return services.AddCors(options =>
            {
                options
                    .AddPolicy("AllowedOrigins",
                        policy =>
                        {
                            policy.WithOrigins(origins)
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .AllowCredentials();
                        });
            });
        }

        private IServiceCollection ConfigureGraphQl(IConfiguration configuration)
        {
            var executionTimeoutSeconds = Math.Max(5, configuration.GetValue<int?>("GraphQL:ExecutionTimeoutSeconds") ?? 45);
            var maxPageSize = Math.Clamp(configuration.GetValue<int?>("GraphQL:MaxPageSize") ?? 100, 10, 250);
            var maxExecutionDepth = Math.Clamp(configuration.GetValue<int?>("GraphQL:MaxExecutionDepth") ?? 8, 4, 20);

            services
                .AddGraphQLServer()
                .AddJsonTypeConverter()
                .AddAuthorization()
                .AddQueryType(d => d.Name("Query"))
                .AddTypeExtension<Queries>()
                .AddType<FlightType>()
                .AddType<FlightSearchMetadataType>()
                .AddType<FlightSearchParametersType>()
                .AddType<FlightOptionType>()
                .AddType<FlightBookingOptionType>()
                .AddType<FlightBookingOptionDetailType>()
                .AddType<FlightBookingRequestType>()
                .AddType<FlightSegmentType>()
                .AddType<AirportType>()
                .AddType<LayoverType>()
                .AddType<FlightPriceInsightsType>()
                .AddType<FlightPriceHistoryPointType>()
                .AddType<AirportInfoType>()
                .AddType<AirportDetailType>()
                .AddType<AirportIdentifierType>()
                .AddType<FlightInputType>()
                .AddType<FlightTypeEnum>()
                .AddType<TravelClassEnum>()
                .AddType<SortByEnum>()
                .AddType<StopsEnum>()
                .AddType<AdvancedSearchOptionsInputType>()
                .AddType<SortingOptionsInputType>()
                .AddType<AdvancedFiltersInputType>()
                .AddType<HotelType>()
                .AddType<HotelDataInputType>()
                .AddType<HotelSortByTypeEnum>()
                .AddType<HotelRatingFilterTypeEnum>()
                .AddType<HotelsPropertyTypeEnum>()
                .AddType<HotelAmenityTypeEnum>()
                .AddType<VacationRentalPropertyTypeEnum>()
                .AddType<VacationRentalAmenityTypeEnum>()
                .AddType<EventType>()
                .AddType<EventDateType>()
                .AddType<VenueType>()
                .AddType<TicketInfoType>()
                .AddType<LocalPlacesType>()
                .AddType<LocalPlacesSearchMetadataType>()
                .AddType<LocalSearchParametersType>()
                .AddType<LocalAdvertisementResultType>()
                .AddType<LocalPlaceResultType>()
                .AddType<GpsCoordinatesType>()
                .AddType<ServiceOptionsType>()
                .AddType<DiscoverMorePlaceType>()
                .AddType<LocalPaginationType>()
                .AddType<LocalSerpApiPaginationType>()
                .AddType<LocalPlacesInputType>()
                .AddType<SearchQueryInputType>()
                .AddType<GeographicLocationInputType>()
                .AddType<LocalPlacesFiltersInputType>()
                .AddType<PaginationInputType>()
                .AddType<LocalPlacesLocalizationInputType>()
                .AddType<MapsType>()
                .AddType<MapsSearchMetadataType>()
                .AddType<MapsSearchParametersType>()
                .AddType<MapsPlaceResultType>()
                .AddType<MapsMenuType>()
                .AddType<MapsExtensionType>()
                .AddType<MapsImageType>()
                .AddType<MapsUserReviewsType>()
                .AddType<MapsReviewSummaryType>()
                .AddType<MapsReviewType>()
                .AddType<MapsRelatedSearchType>()
                .AddType<MapsPopularTimesType>()
                .AddType<MapsLiveHashType>()
                .AddType<MapsEventType>()
                .AddType<MapsEventDateType>()
                .AddType<MapsTicketInfoType>()
                .AddType<MapsQAType>()
                .AddType<MapsQuestionType>()
                .AddType<MapsAnswerType>()
                .AddType<MapsUserType>()
                .AddType<MapsAtThisPlaceType>()
                .AddType<MapsPlaceTypeType>()
                .AddType<MapsSubPlaceType>()
                .AddType<MapsAdmissionType>()
                .AddType<MapsAdmissionOptionType>()
                .AddType<MapsExperienceType>()
                .AddType<MapsPostType>()
                .AddType<MapsWeatherType>()
                .AddType<MapsAtLocationType>()
                .AddType<MapsInputType>()
                .AddType<MapsLocalizationInputType>()
                .AddType<MapsPaginationInputType>()
                .AddType<MapsSearchQueryInputType>()
                .AddType<MapsDirectionsType>()
                .AddType<MapsPlaceResultsType>()
                .AddType<MapsDirectionsSearchParametersType>()
                .AddType<MapsPlaceResultsSearchParametersType>()
                .AddType<MapsDirectionsInputType>()
                .AddType<MapsPlaceResultsInputType>()
                .AddType<PlaceReviewsType>()
                .AddType<TripAdvisorSearchType>()
                .AddType<TripAdvisorPlaceType>()
                .AddType<TripAdvisorSearchMetadataType>()
                .AddType<TripAdvisorPlaceSearchMetadataType>()
                .AddType<TripAdvisorSearchInformationType>()
                .AddType<TripAdvisorSearchParametersType>()
                .AddType<TripAdvisorPlaceSearchParametersType>()
                .AddType<TripAdvisorSearchInputType>()
                .AddType<TripAdvisorPlaceInputType>()
                .AddType<YouTubeSearchType>()
                .AddType<YouTubeSearchMetadataType>()
                .AddType<YouTubeSearchParametersType>()
                .AddType<YouTubeSearchInformationType>()
                .AddType<YouTubeSearchInputType>()
                .AddType<GoogleLightSearchType>()
                .AddType<GoogleLightSearchMetadataType>()
                .AddType<GoogleLightSearchParametersType>()
                .AddType<GoogleLightSearchInformationType>()
                .AddType<GoogleLightSearchInputType>()
                .AddType<OpenTableReviewsType>()
                .AddType<OpenTableSearchMetadataType>()
                .AddType<OpenTableSearchParametersType>()
                .AddType<OpenTableSearchInformationType>()
                .AddType<OpenTableReviewsSummaryType>()
                .AddType<OpenTableRatingsSummaryType>()
                .AddType<OpenTableRatingBreakdownType>()
                .AddType<OpenTableAwardType>()
                .AddType<OpenTableReviewType>()
                .AddType<OpenTableReviewRatingsType>()
                .AddType<OpenTableReviewUserType>()
                .AddType<OpenTableReviewHelpfulnessType>()
                .AddType<OpenTableReviewImageType>()
                .AddType<OpenTableReviewImageVariantType>()
                .AddType<OpenTableReviewResponseType>()
                .AddType<OpenTableSerpApiPaginationType>()
                .AddType<OpenTableReviewsInputType>()
                .AddType<YelpSearchType>()
                .AddType<YelpPlaceType>()
                .AddType<YelpPlaceFullMenuType>()
                .AddType<YelpReviewsType>()
                .AddType<YelpSearchMetadataType>()
                .AddType<YelpSearchInformationType>()
                .AddType<YelpSearchParametersType>()
                .AddType<YelpPlaceSearchParametersType>()
                .AddType<YelpReviewsSearchParametersType>()
                .AddType<YelpSearchInputType>()
                .AddType<YelpPlaceInputType>()
                .AddType<YelpPlaceFullMenuInputType>()
                .AddType<YelpReviewsInputType>()
                .AddProjections()
                .AddFiltering()
                .AddSorting()
                .AddErrorFilter<GraphQlErrorFilter>()
                .ModifyPagingOptions(options => options.MaxPageSize = maxPageSize)
                .ModifyRequestOptions(options => options.ExecutionTimeout = TimeSpan.FromSeconds(executionTimeoutSeconds))
                .AddMaxExecutionDepthRule(maxExecutionDepth)
                .ModifyCostOptions(options =>
                {
                    options.MaxFieldCost = 2000;
                    options.MaxTypeCost = 2000;
                    options.EnforceCostLimits = true;
                });

            return services;
        }

        private IServiceCollection ConfigureAuthentication(IConfiguration configuration)
        {
            var jwtConfigurationSection = configuration.GetSection("Jwt");
            services.Configure<Jwt>(jwtConfigurationSection);
            var jwtSettings = jwtConfigurationSection.Get<Jwt>() ??
                              throw new InvalidOperationException("Configuration section 'Jwt' is required.");

            var googleAuthSection = configuration.GetSection("GoogleAuth");
            services.Configure<GoogleAuth>(googleAuthSection);
            var googleAuthSettings = googleAuthSection.Get<GoogleAuth>() ??
                                     throw new InvalidOperationException("Configuration section 'GoogleAuth' is required.");

            var jwtIssuer = RequireSetting(jwtSettings.Issuer, "Jwt:Issuer");
            var jwtAudience = RequireSetting(jwtSettings.Audience, "Jwt:Audience");
            var jwtKey = RequireSetting(jwtSettings.Key, "Jwt:Key");
            var googleClientIds = ResolveGoogleClientIds(googleAuthSettings.ClientId);
            var googleClientId = googleClientIds[0];
            var googleClientSecret = NormalizeConfiguredValue(googleAuthSettings.ClientSecret);

            var authBuilder = services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyAuthenticationHandler.SchemeName, _ => { })
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = CreateSecurityKeyWithId(jwtKey),
                        ClockSkew = TimeSpan.Zero
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            if (!string.IsNullOrWhiteSpace(context.Token))
                            {
                                return Task.CompletedTask;
                            }

                            if (context.Request.Cookies.TryGetValue(AuthCookieHelper.AccessTokenCookieName, out var accessToken) &&
                                !string.IsNullOrWhiteSpace(accessToken))
                            {
                                context.Token = accessToken;
                            }

                            return Task.CompletedTask;
                        },
                        OnAuthenticationFailed = context =>
                        {
                            if (context.HttpContext.Request.Path.StartsWithSegments("/graphql"))
                            {
                                var authLogger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILoggerFactory>()
                                    .CreateLogger("GraphQLJwtAuth");
                                authLogger.LogWarning(
                                    context.Exception,
                                    "JWT authentication failed for GraphQL request. Path={Path} TraceId={TraceId}",
                                    context.HttpContext.Request.Path,
                                    context.HttpContext.TraceIdentifier);
                            }

                            return Task.CompletedTask;
                        },
                        OnTokenValidated = async context =>
                        {
                            var principal = context.Principal;
                            if (principal is null)
                            {
                                context.Fail("Token principal is missing.");
                                return;
                            }

                            var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                                          ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                            if (!long.TryParse(subject, out var userId))
                            {
                                context.Fail("Token subject is invalid.");
                                return;
                            }

                            var securityStamp = principal.FindFirst(SecurityStampClaimType)?.Value;
                            if (string.IsNullOrWhiteSpace(securityStamp))
                            {
                                context.Fail("Token security stamp is missing.");
                                return;
                            }

                            var userRepository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                            var userSnapshot = await userRepository.GetAuthSnapshotByIdAsync(userId, context.HttpContext.RequestAborted);
                            if (userSnapshot is null || string.IsNullOrWhiteSpace(userSnapshot.SecurityStamp))
                            {
                                context.Fail("User identity is invalid.");
                                return;
                            }

                            if (!userSnapshot.IsActive)
                            {
                                context.Fail("User is inactive.");
                                return;
                            }

                            if (!string.Equals(userSnapshot.SecurityStamp, securityStamp, StringComparison.Ordinal))
                            {
                                context.Fail("Token has been revoked.");
                                return;
                            }

                            if (context.HttpContext.Request.Path.StartsWithSegments("/graphql"))
                            {
                                var authLogger = context.HttpContext.RequestServices
                                    .GetRequiredService<ILoggerFactory>()
                                    .CreateLogger("GraphQLJwtAuth");
                                authLogger.LogInformation(
                                    "Validated JWT for GraphQL request. Path={Path} TraceId={TraceId} UserId={UserId}",
                                    context.HttpContext.Request.Path,
                                    context.HttpContext.TraceIdentifier,
                                    userId);
                            }
                        }
                    };
                });

            if (!string.IsNullOrWhiteSpace(googleClientSecret))
            {
                authBuilder.AddGoogle(options =>
                {
                    options.ClientId = googleClientId;
                    options.ClientSecret = googleClientSecret;
                    options.CallbackPath = "/auth/google-callback";
                    options.Scope.Add("email");
                    options.Scope.Add("profile");
                    options.SaveTokens = true;
                });
            }

            services.AddAuthorization(options =>
            {
                options.AddPolicy("GraphQLAuth", policy =>
                {
                    policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                });
                options.AddPolicy("Admin", policy => { policy.RequireClaim(ClaimTypes.Role, "Admin"); });
                options.AddPolicy("MetricsRead", policy =>
                {
                    policy.RequireAssertion(context =>
                    {
                        if (context.Resource is not HttpContext httpContext)
                        {
                            return false;
                        }

                        var internalAccessValidator = httpContext.RequestServices.GetRequiredService<IInternalAccessValidator>();
                        return internalAccessValidator.Validate(httpContext).IsAuthorized;
                    });
                });
            });

            return services;
        }

        private IServiceCollection ConfigureHangfireClient(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("db")
                ?? throw new InvalidOperationException("Connection string 'db' is required for Hangfire.");

            services.AddHangfire(globalConfiguration => globalConfiguration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        PrepareSchemaIfNecessary = true,
                        SchemaName = "Hangfire"
                    }));

            return services;
        }

        private IServiceCollection ConfigureServices()
        {
            services.AddHttpClient();
            services.AddHttpClient<ITripQueryHistorySummaryExpander, TripQueryHistorySummaryExpander>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(8);
            });
            services.AddHttpClient<ITelegramChatNotifier, TelegramChatNotifier>(client =>
            {
                client.BaseAddress = new Uri("https+http://bot");
                client.Timeout = TimeSpan.FromSeconds(5);
            });

            return services
                .AddSingleton<IApiKeyValidator, ApiKeyValidator>()
                .AddSingleton<IInternalAccessValidator, InternalAccessValidator>()
                .AddScoped<ICurrentRequestUserProvider, CurrentRequestUserProvider>()
                .AddScoped<RequireUsernameFilter>()
                .AddScoped<CookieAntiforgeryFilter>()
                .AddScoped<IAuthResponseBuilder, AuthResponseBuilder>()
                .AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>()
                .AddScoped<IRefreshTokenRequestResolver, RefreshTokenRequestResolver>()
                .AddScoped<IAuthenticationService, AuthenticationService>()
                .AddScoped<ICredentialValidator, CredentialValidator>()
                .AddScoped<IClientIpResolver, ClientIpResolver>()
                .AddScoped<IScheduledEventQueryRepository, ScheduledEventQueryRepository>()
                .AddScoped<ISearchResponseFilter<GetEventResponseDTO>, EventResponseFilter>();
        }

        private IServiceCollection ConfigureForwardedHeaders(IConfiguration configuration)
        {
            var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                foreach (var proxy in knownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var address))
                    {
                        options.KnownProxies.Add(address);
                    }
                }
            });

            return services;
        }

        private IServiceCollection ConfigureRateLimiting(IConfiguration configuration)
        {
            return services
                .Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"))
                .Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"))
                .AddSingleton<IIpPolicyStore, DistributedCacheIpPolicyStore>()
                .AddSingleton<IRateLimitCounterStore, DistributedCacheRateLimitCounterStore>()
                .AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>()
                .AddDistributedRateLimiting();
        }
    }

    private static SymmetricSecurityKey CreateSecurityKeyWithId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("JWT signing key is not configured. Please provide a valid key in configuration.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(key);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT signing key must be at least 32 bytes (256 bits) for secure HMAC-SHA256 signing. " +
                $"Current key length: {keyBytes.Length} bytes. Please configure a stronger key.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);
        var keyHash = SHA256.HashData(keyBytes);
        var base64Hash = Convert.ToBase64String(keyHash);
        securityKey.KeyId = base64Hash.Length >= 16 ? base64Hash[..16] : base64Hash;

        return securityKey;
    }

    private static string[] ResolveCorsOrigins(string? configuredValue)
    {
        var configuredOrigins = SplitConfiguredValues(configuredValue)
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configuredOrigins.Length == 0 ? _localDefaultCorsOrigins : configuredOrigins;
    }

    private static string[] ResolveGoogleClientIds(string? configuredValue)
    {
        var googleClientIds = SplitConfiguredValues(configuredValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return googleClientIds.Length == 0
            ? throw new InvalidOperationException("Configuration setting 'GoogleAuth:ClientId' is required.")
            : googleClientIds;
    }

    private static string? NormalizeConfiguredValue(string? value) => SplitConfiguredValues(value).FirstOrDefault();

    private static IEnumerable<string> SplitConfiguredValues(string? configuredValue) =>
        (configuredValue ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(value => !value.Contains('{') && !value.Contains('}'));

    private static string RequireSetting(string? value, string path) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"Configuration setting '{path}' is required.");

    extension(IServiceCollection services)
    {
        private IServiceCollection ConfigureSettings(IConfiguration configuration) =>
            services
                .Configure<SerpApiSettings>(configuration.GetSection("SerpApiSettings"))
                .Configure<EmailSettings>(configuration.GetSection("EmailSettings"))
                .Configure<PaymentSettings>(configuration.GetSection("PaymentSettings"))
                .Configure<StripeApiSettings>(configuration.GetSection("PaymentSettings:Stripe"))
                .Configure<CachingSettings>(configuration.GetSection("Caching"));

        private IServiceCollection ConfigureHttps(IHostEnvironment environment)
        {
            if (!environment.IsDevelopment())
            {
                services.AddHsts(options =>
                {
                    options.Preload = true;
                    options.IncludeSubDomains = true;
                    options.MaxAge = TimeSpan.FromDays(365);
                });
            }

            // Configure HTTPS redirection
            services.AddHttpsRedirection(options =>
            {
                options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
                options.HttpsPort = environment.IsDevelopment() ? 5101 : 443;
            });

            return services;
        }

        private IServiceCollection ConfigureMetricsAndTraces()
        {
            services
                .AddOpenTelemetry()
                .WithMetrics(builder => builder
                    .AddMeter(MetricConstants.ApplicationName)
                    .AddMeter("Microsoft.EntityFrameworkCore"))
                .WithTracing(builder => builder
                    .AddSource(MetricConstants.ApplicationName)
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddHangfireInstrumentation());

            return services;
        }

        private IServiceCollection ConfigureResponseCompression()
        {
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
                    "application/json",
                    "text/json",
                    "application/graphql-response+json"
                ]);
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = System.IO.Compression.CompressionLevel.Fastest;
            });

            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = System.IO.Compression.CompressionLevel.Fastest;
            });

            return services;
        }

        private IServiceCollection ConfigureOutputCaching()
        {
            services.AddOutputCache();
            return services;
        }
    }
}
