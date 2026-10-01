using Aspire.Hosting.Testing;
using DigitalBrain.Aspire.Hosting;

namespace IntoChat.Tests.E2E.Composition;

// Builds the real AppHost model without starting any resource. Aspire, Roslyn, DotNet, Coding,
// and CSharp are composed for every profile.
public sealed class ProfileCompositionFacts
{
    private static readonly string[] ToolingModules = ["Aspire", "Roslyn", "DotNet", "Coding", "CSharp"];

    private static readonly string[] UserPathModules =
        ["AI", "Memory", "ClickHouse", "Supabase", "Time", "Gmail", "Salesforce", "GitHub", "Flutter"];

    [Theory]
    [InlineData("developer")]
    [InlineData("product")]
    public async Task EveryProfileComposesToolingAndUserPathModules(string profile)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>([$"IntoChat:Profile={profile}"], TestContext.Current.CancellationToken);
        var names = appHost.Resources.Select(resource => resource.Name).ToArray();
        foreach (var module in ToolingModules.Concat(UserPathModules)) { Assert.Contains(module, names); }
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("product")]
    public async Task EveryProfileMatchesTheReferenceCompositionModuleSet(string profile)
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(
            [$"IntoChat:Profile={profile}"], TestContext.Current.CancellationToken);
        var productModules = appHost.Resources.SelectMany(resource => resource.Annotations.OfType<BrainModuleAnnotation>())
            .Select(module => module.ModuleType.FullName).Order().ToArray();
        var referenceModules = ReferenceBrain.Create().BuildComposition().Modules
            .Select(module => module.ModuleType.FullName).Order().ToArray();
        Assert.Equal(productModules, referenceModules);
    }
}
