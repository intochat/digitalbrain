using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Blobs;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
namespace DigitalBrain.FeatureHost;

internal static class FeatureHostServices
{
    public static IServiceCollection AddDigitalBrainFeatureHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var origin = Required(configuration["DigitalBrain:FeatureHost:InternalOrigin"], "internal origin");
        var token = Required(configuration["DigitalBrain:FeatureHost:InternalToken"], "internal token");
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("DigitalBrain:FeatureHost:InternalOrigin must be an absolute HTTP origin.");
        if (token.Length < 32)
            throw new InvalidOperationException("DigitalBrain:FeatureHost:InternalToken must contain at least 32 characters.");
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFeatureHostRecycle, HostedFeatureHostRecycle>();
        services.TryAddSingleton(serviceProvider => new FeatureReleaseManager(serviceProvider, serviceProvider.GetRequiredService<IFeatureHostRecycle>(), configuration["DigitalBrain:FeatureHost:CacheDirectory"]));
        services.TryAddSingleton<IFeatureArtifactCatalog>(serviceProvider => new BlobFeatureArtifactCatalog(serviceProvider.GetRequiredKeyedService<BlobServiceClient>("features"), configuration["DigitalBrain:FeatureHost:ArtifactCacheDirectory"]));
        services.TryAddSingleton<IFeatureWorkSource, OrleansFeatureWorkSource>();
        services.TryAddSingleton<IFeatureRunContextFactory, CapabilityFeatureRunContextFactory>();
        services.TryAddSingleton(new FeatureExecutionOptions($"feature-host-{Environment.MachineName}-{Guid.NewGuid():N}", TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)));
        services.TryAddSingleton<IFeatureCapabilityClient>(_ =>
        {
            var client = new HttpClient();
            client.BaseAddress = originUri;
            client.DefaultRequestHeaders.Add("X-DigitalBrain-Internal-Token", token);
            client.Timeout = Timeout.InfiniteTimeSpan;
            return new HttpFeatureCapabilityClient(client);
        });
        services.TryAddSingleton<IGmailMessageReader, FeatureGmailMessageReader>();
        services.TryAddSingleton<IGmailMailboxReader, FeatureGmailMailboxReader>();
        services.TryAddSingleton<IGmailSendProposer, FeatureGmailSendProposer>();
        services.TryAddSingleton<ISalesforceRecordReader, FeatureSalesforceRecordReader>();
        services.TryAddSingleton<ISalesforceUpdateProposer, FeatureSalesforceUpdateProposer>();
        services.TryAddSingleton<FeatureExecutionWorker>();
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<FeatureExecutionWorker>());
        return services;
    }
    private static string Required(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2_048 || value.Any(char.IsControl))
            throw new InvalidOperationException($"A bounded FeatureHost {label} is required.");
        return value;
    }
}
internal sealed class HttpFeatureCapabilityClient(HttpClient client) : IFeatureCapabilityClient, IDisposable
{
    public async Task<JsonElement> ExecuteAsync(CapabilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await client.PostAsJsonAsync("/internal/features/capabilities/execute", request, FeatureJson.Options, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("RuntimeHost denied or failed the feature capability operation.");
        var result = await response.Content.ReadFromJsonAsync<FeatureCapabilityResponse>(FeatureJson.Options, cancellationToken) ?? throw new InvalidOperationException("RuntimeHost returned no capability result.");
        return result.Payload.Clone();
    }
    private sealed record FeatureCapabilityResponse(string Kind, JsonElement Payload);
    public void Dispose() => client.Dispose();
}
internal sealed class HostedFeatureHostRecycle(IHostApplicationLifetime lifetime) : IFeatureHostRecycle
{
    public void RequestRecycle() => lifetime.StopApplication();
}
