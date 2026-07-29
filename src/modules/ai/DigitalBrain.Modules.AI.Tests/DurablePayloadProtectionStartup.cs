using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class DurablePayloadProtectionStartup
{
    private const string ProtectionKey = "DigitalBrain:Security:StateProtectionKey";
    private const string ProtectorType = "DigitalBrain.Security.IDurablePayloadProtector";
    private static readonly string ValidKey = Convert.ToBase64String(new byte[32]);

    public static TheoryData<string?, Type> InvalidKeys =>
    [
        (null, typeof(InvalidOperationException)),
        ("not-base64", typeof(ArgumentException)),
        (Convert.ToBase64String(new byte[31]), typeof(ArgumentException)),
    ];

    [Theory(DisplayName = "invalid durable protection keys fail during service composition")]
    [MemberData(nameof(InvalidKeys))]
    public void InvalidKeysFailDuringServiceComposition(string? encodedKey, Type expectedExceptionType)
    {
        var result = Compose(encodedKey, resolveProtector: false);

        Assert.NotNull(result.CompositionException);
        Assert.IsType(expectedExceptionType, result.CompositionException);
    }

    [Fact(DisplayName = "a 256-bit Base64 durable protection key registers and resolves")]
    public void ValidKeyRegistersAndResolves()
    {
        var result = Compose(ValidKey, resolveProtector: true);

        Assert.Null(result.CompositionException);
        Assert.Null(result.ResolutionException);
    }

    private static CompositionResult Compose(string? encodedKey, bool resolveProtector)
    {
        var values = new Dictionary<string, string?>
        {
            ["DigitalBrain:Modules:0"] = ((ICompiledModule)new AIModule()).Id.Value,
        };

        if (encodedKey is not null)
        {
            values[ProtectionKey] = encodedKey;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(new HostBuilderContext(new Dictionary<object, object>())
        {
            Configuration = configuration,
        });
        var builder = new CompositionSiloBuilder(services, configuration);
        var module = (ICompiledModule)new AIModule();
        var compositionException = Record.Exception(() =>
            DigitalBrainRuntime.Add(builder, [module]));

        if (!resolveProtector || compositionException is not null)
        {
            return new(compositionException, null);
        }

        var protectorService = services.Single(descriptor =>
            descriptor.ServiceType.FullName == ProtectorType);
        using var provider = services.BuildServiceProvider();
        var resolutionException = Record.Exception(() =>
            provider.GetRequiredService(protectorService.ServiceType));
        return new(compositionException, resolutionException);
    }

    private sealed class CompositionSiloBuilder(IServiceCollection services, IConfiguration configuration) : ISiloBuilder
    {
        public IServiceCollection Services => services;

        public IConfiguration Configuration => configuration;
    }

    private sealed record CompositionResult(Exception? CompositionException, Exception? ResolutionException);
}
