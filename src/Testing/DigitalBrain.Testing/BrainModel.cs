using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Testing;

// Tier 1: builds the AppHost model without starting any resource, for
// topology/env-var assertions that run in milliseconds.
public sealed class BrainModel : IAsyncDisposable
{
    private readonly DistributedApplication _app;

    private BrainModel(DistributedApplication app, IReadOnlyList<IResource> resources)
    {
        _app = app;
        Resources = resources;
    }

    public IReadOnlyList<IResource> Resources { get; }

    public static async Task<BrainModel> BuildAsync<TAppHost>(params string[] args)
        where TAppHost : class
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<TAppHost>(args, static (_, _) => { })
            .ConfigureAwait(false);

        foreach (var parameter in builder.Resources.OfType<ParameterResource>())
        {
            builder.Configuration[$"Parameters:{parameter.Name}"] = "test";
        }

        // Captured before Build() — IDistributedApplicationTestingBuilder.Resources is an
        // IResourceCollection (IList<IResource>, not IReadOnlyList<IResource>), and every
        // resource the AppHost registers is already present by the time CreateAsync returns.
        var resources = builder.Resources.ToList();
        var app = await builder.BuildAsync().ConfigureAwait(false);
        return new BrainModel(app, resources);
    }

    public IResource Resource(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var resource in Resources)
        {
            if (string.Equals(resource.Name, name, StringComparison.Ordinal))
            {
                return resource;
            }
        }

        var available = string.Join(", ", Resources.Select(static r => r.Name));
        throw new InvalidOperationException($"No resource named '{name}'. Available: [{available}].");
    }

    public async Task<IReadOnlyDictionary<string, string>> RenderedEnvironmentAsync(string resourceName)
    {
        var resource = Resource(resourceName);
        var configuration = await ExecutionConfigurationBuilder.Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(new(DistributedApplicationOperation.Publish), NullLogger.Instance)
            .ConfigureAwait(false);
        return configuration.EnvironmentVariables.ToDictionary();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
