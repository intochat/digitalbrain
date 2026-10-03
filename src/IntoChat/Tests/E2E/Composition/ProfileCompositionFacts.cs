using Aspire.Hosting.Testing;
using DigitalBrain.Aspire.Hosting;

namespace IntoChat.Tests.E2E.Composition;

// Builds the real AppHost model without starting any resource. The CSharp module owns
// compilation and authoring; both product profiles compose the same connector set.
public sealed class ProfileCompositionFacts
{
    private static readonly string[] ToolingModules = ["aspire", "csharp", "csharp-authoring"];

    private static readonly string[] UserPathModules =
        ["ai", "memory", "clickhouse", "supabase", "time", "gmail", "salesforce", "github", "flutter"];

    [Theory]
    [InlineData("developer")]
    [InlineData("product")]
    public async Task EveryProfileComposesToolingAndUserPathModules(string profile)
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>([$"IntoChat:Profile={profile}"], TestContext.Current.CancellationToken);
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
            .Select(module => module.ModuleId).Order().ToArray();
        var referenceModules = ReferenceBrain.Create().BuildComposition().Modules
            .Select(module => DigitalBrain.Kernel.ModuleIdentity.Get(module.ModuleType)).Order().ToArray();
        Assert.Equal(productModules, referenceModules);
    }
}
