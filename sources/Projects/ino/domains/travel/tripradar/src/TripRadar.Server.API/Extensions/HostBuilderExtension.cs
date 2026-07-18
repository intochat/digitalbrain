using System.Globalization;
using System.Reflection;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Elasticsearch;
using Serilog.Sinks.Elasticsearch;

namespace TripRadar.Server.API.Extensions;

internal static class HostBuilderExtension
{
    extension(IHostBuilder hostBuilder)
    {
        public void ConfigureHostBuilder(IConfiguration configuration, IWebHostEnvironment environment) =>
            hostBuilder.AddSerilog(configuration, environment);

        private void AddSerilog(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "TripRadar";
            var environmentName = environment.EnvironmentName;

            var serilogConfiguration = BuildSerilogConfiguration(configuration);
            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .Enrich.WithProperty("Application", assemblyName)
                .Enrich.WithProperty("Environment", environmentName)
                .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
                .ReadFrom.Configuration(serilogConfiguration);

            var elasticSearchUri = configuration["ElasticConfiguration:Uri"];
            if (TryGetElasticUri(elasticSearchUri ?? string.Empty, out var elasticUri))
            {
                loggerConfiguration = loggerConfiguration.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(elasticUri)
                {
                    AutoRegisterTemplate = true,
                    IndexFormat =
                        $"{assemblyName.ToLower().Replace(".", "-")}-{environmentName.ToLower()}-{DateTime.UtcNow:yyyy.MM}",
                    DetectElasticsearchVersion = true,
                    RegisterTemplateFailure = RegisterTemplateRecovery.IndexAnyway,
                    AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv8,
                    EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                                       EmitEventFailureHandling.WriteToFailureSink |
                                       EmitEventFailureHandling.RaiseCallback,
                    NumberOfReplicas = 1,
                    NumberOfShards = 2,
                    BufferLogShippingInterval = TimeSpan.FromSeconds(5),
                    InlineFields = true,
                    CustomFormatter = new ElasticsearchJsonFormatter()
                });
            }

            Log.Logger = loggerConfiguration.CreateLogger();

            hostBuilder.UseSerilog();
        }
    }

    private static bool TryGetElasticUri(string rawValue, out Uri elasticUri)
    {
        elasticUri = null!;

        if (rawValue.Contains("${", StringComparison.Ordinal) || rawValue.Contains('{'))
            return false;

        if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        elasticUri = parsed;
        return true;
    }

    private static IConfiguration BuildSerilogConfiguration(IConfiguration configuration)
    {
        var writeToSection = configuration.GetSection("Serilog:WriteTo");
        var invalidPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in writeToSection.GetChildren())
        {
            var name = child["Name"];
            if (!string.Equals(name, "Elasticsearch", StringComparison.OrdinalIgnoreCase))
                continue;

            var nodeUris = child["Args:nodeUris"];
            if (string.IsNullOrWhiteSpace(nodeUris) || !TryGetElasticUri(nodeUris, out _))
                invalidPrefixes.Add($"Serilog:WriteTo:{child.Key}");
        }

        if (invalidPrefixes.Count == 0) return configuration;

        var filtered = configuration.AsEnumerable().Where(kvp => !invalidPrefixes.Any(prefix => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        return new ConfigurationBuilder()
            .AddInMemoryCollection(filtered)
            .Build();
    }
}
