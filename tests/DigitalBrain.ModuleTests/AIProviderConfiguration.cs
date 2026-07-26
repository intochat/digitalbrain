using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AIProviderConfiguration
{
    private const string OllamaEndpointKey = "DigitalBrain:AI:Ollama:Endpoint";
    private static readonly string ValidStateProtectionKey = Convert.ToBase64String(new byte[32]);

    public static TheoryData<string?> InvalidOllamaEndpoints =>
    [
        (string?)null,
        "",
        " ",
        "not a URI",
        "localhost:11434",
        "file:///tmp/ollama",
    ];

    public static TheoryData<string> ValidOllamaEndpoints =>
    [
        "http://ollama.example.test:11434",
        "https://ollama.example.test:11435",
    ];

    [Theory(DisplayName = "missing, blank, and malformed Ollama endpoints fail before client contact")]
    [MemberData(nameof(InvalidOllamaEndpoints))]
    public void InvalidOllamaEndpointsFailBeforeClientContact(string? endpoint)
    {
        using var provider = Compose(endpoint);

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IChatClient>(typeof(Llama32)));

        Assert.Contains(OllamaEndpointKey, exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "an explicit HTTP(S) Ollama endpoint resolves only the selected Llama32 client")]
    [MemberData(nameof(ValidOllamaEndpoints))]
    public void ExplicitOllamaEndpointResolvesSelectedClient(string endpoint)
    {
        using var provider = Compose(endpoint);

        var client = provider.GetRequiredKeyedService<IChatClient>(typeof(Llama32));
        var ollama = Assert.IsAssignableFrom<IOllamaApiClient>(client);

        Assert.Equal(new Uri(endpoint), ollama.Uri);
        Assert.False(string.Equals("localhost", ollama.Uri.Host, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("llama3.2", ollama.SelectedModel);
    }

    private static ServiceProvider Compose(string? endpoint)
    {
        var values = new Dictionary<string, string?>
        {
            ["DigitalBrain:Modules:0"] = ((ICompiledModule)new AIModule()).Id.Value,
            ["DigitalBrain:Security:StateProtectionKey"] = ValidStateProtectionKey,
        };

        if (endpoint is not null)
        {
            values[OllamaEndpointKey] = endpoint;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new HostBuilderContext(new Dictionary<object, object>())
        {
            Configuration = configuration,
        });
        var builder = new CompositionSiloBuilder(services, configuration);
        var module = (ICompiledModule)new AIModule();

        DigitalBrainRuntime.Add(builder, siloLabel: null, [module]);
        return services.BuildServiceProvider();
    }

    private sealed class CompositionSiloBuilder(
        IServiceCollection services,
        IConfiguration configuration) : ISiloBuilder
    {
        public IServiceCollection Services => services;

        public IConfiguration Configuration => configuration;
    }
}
