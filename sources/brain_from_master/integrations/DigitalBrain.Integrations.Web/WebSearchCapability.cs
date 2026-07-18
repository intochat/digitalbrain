using System.Text.Json;
using DigitalBrain.Integrations.Web.Contracts;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;

namespace DigitalBrain.Integrations.Web;

internal sealed class WebSearchCapabilityHandler(IGrainFactory grains) : ICapabilityHandler
{
    public string CapabilityId => WebSearchCapabilityIds.Search;
    public int CapabilityVersion => 1;
    public CapabilityOperationKind OperationKind => CapabilityOperationKind.Query;

    public async Task<JsonElement> ExecuteAsync(
        CapabilityRequest request,
        CapabilityGrant grant,
        CancellationToken cancellationToken = default)
    {
        var query = request.Payload.Deserialize<WebSearchRequest>()
            ?? throw new ArgumentException("The web search payload is invalid.", nameof(request));
        var snapshot = await grains
            .GetGrain<IWebSearch>(request.OwnerId.Value)
            .SearchAsync(
                new WebSearchQuery(query.Query, query.MaximumResults),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return JsonSerializer.SerializeToElement(
            new WebSearchResponse(snapshot.Results
                .Select(static result => new WebSearchResult(
                    result.Title,
                    result.Url,
                    result.Snippet))
                .ToArray()));
    }
}

internal sealed class WebSearchCapabilityDescriptorSource : ICapabilityDescriptorSource
{
    public IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
    [
        new(
            WebSearchCapabilityIds.Search,
            1,
            "Search the web",
            "Searches current public web sources and returns bounded titles, URLs, and snippets.",
            ["Research Northstar Robotics.", "Find current public information about a company."],
            ["web.search"],
            [],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true)
    ];
}

public static class WebSearchServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalBrainWebSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(_ => new BraveWebSearchClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
            configuration["DigitalBrain:WebSearch:BraveApiKey"]));
        services.AddSingleton<ICapabilityHandler, WebSearchCapabilityHandler>();
        services.AddSingleton<ICapabilityDescriptorSource, WebSearchCapabilityDescriptorSource>();
        return services;
    }
}
