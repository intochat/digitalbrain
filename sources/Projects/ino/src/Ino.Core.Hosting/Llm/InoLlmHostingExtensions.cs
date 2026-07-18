using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Binds <see cref="InoLlmOptions"/> from the <c>Ino:Llm</c> configuration
/// section and registers the selected <see cref="IChatClient"/> plus a shared
/// <see cref="IReasoningProbe"/> singleton. For v0.1 only the <c>bdd-mock</c>
/// provider is implemented — other providers throw
/// <see cref="NotSupportedException"/> at resolution time so a silo boots
/// green in the common case but fails loudly if misconfigured.
///
/// <para><c>defaultFeatureDirectories</c> is how a silo host contributes the
/// well-known spots it expects to find <c>*.feature</c> files in (e.g. its
/// content root + <c>Features</c>). The loader always adds
/// <c>AppContext.BaseDirectory/Features</c> so neuron projects that copy
/// their <c>Features/*.feature</c> to output find the scenarios without the
/// host having to know about each neuron.</para>
/// </summary>
public static class InoLlmServiceCollectionExtensions
{
    public static IServiceCollection AddInoLlm(
        this IServiceCollection services,
        IConfiguration configuration,
        params string[] defaultFeatureDirectories)
    {
        services.Configure<InoLlmOptions>(configuration.GetSection(InoLlmOptions.SectionName));
        services.AddSingleton<IReasoningProbe, InMemoryReasoningProbe>();
        services.AddSingleton<IChatClient>(sp => BuildChatClient(sp, defaultFeatureDirectories));
        return services;
    }

    static IChatClient BuildChatClient(IServiceProvider sp, string[] defaultFeatureDirectories)
    {
        var options = sp.GetService<Microsoft.Extensions.Options.IOptions<InoLlmOptions>>()?.Value
            ?? new InoLlmOptions();
        var provider = options.Provider?.Trim().ToLowerInvariant() ?? InoLlmOptions.BddMock;

        return provider switch
        {
            InoLlmOptions.BddMock => BuildBddMock(sp, options, defaultFeatureDirectories),
            InoLlmOptions.AzureOpenAI => throw new NotSupportedException(
                "Ino.Llm.Provider=azure-openai is deferred to a post-v0.1 slice."),
            InoLlmOptions.Anthropic => throw new NotSupportedException(
                "Ino.Llm.Provider=anthropic is deferred to a post-v0.1 slice."),
            _ => throw new NotSupportedException(
                $"Unknown Ino.Llm.Provider '{options.Provider}'. Expected one of: {InoLlmOptions.BddMock}, {InoLlmOptions.AzureOpenAI}, {InoLlmOptions.Anthropic}."),
        };
    }

    static BddMockChatClient BuildBddMock(IServiceProvider sp, InoLlmOptions options, string[] defaults)
    {
        var probe = sp.GetRequiredService<IReasoningProbe>();
        var log = sp.GetService<ILoggerFactory>()?.CreateLogger<BddMockChatClient>();

        var paths = new List<string>();
        paths.AddRange(defaults);
        paths.Add(Path.Combine(AppContext.BaseDirectory, "Features"));
        paths.AddRange(options.AdditionalFeaturePaths);

        var result = BddScenarioLoader.LoadFromDirectories(paths);
        if (log is not null)
        {
            log.LogInformation(
                "bdd-mock loaded {ScenarioCount} scenario(s) from {PathCount} path(s); skipped {SkippedCount}",
                result.Scenarios.Count, paths.Count, result.SkippedReasons.Count);
            foreach (var reason in result.SkippedReasons)
                log.LogWarning("bdd-mock skipped scenario: {Reason}", reason);
        }

        return new BddMockChatClient(result.Scenarios, probe, log);
    }
}
