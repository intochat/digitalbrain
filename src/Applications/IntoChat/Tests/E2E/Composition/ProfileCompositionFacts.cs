using Aspire.Hosting.Testing;

namespace IntoChat.Tests.E2E.Composition;

// Builds the real AppHost model without starting any resource. Aspire, Roslyn, DotNet, Coding,
// and CSharp are composed for every profile.
public sealed class ProfileCompositionFacts
{
    private static readonly string[] ToolingModules = ["Aspire", "Roslyn", "DotNet", "Coding", "CSharp"];

    private static readonly string[] UserPathModules =
        ["AI", "Memory", "ClickHouse", "Supabase", "Time", "Gmail", "Salesforce", "GitHub", "Flutter"];

    [Fact]
    public async Task DeveloperProfileComposesToolingModules()
    {
        var names = await ResourceNames(["IntoChat:Profile=developer"], TestContext.Current.CancellationToken);
        foreach (var module in ToolingModules) { Assert.Contains(module, names); }
        foreach (var module in UserPathModules) { Assert.Contains(module, names); }
    }

    [Fact]
    public async Task ProductProfileComposesToolingModules()
    {
        var names = await ResourceNames(["IntoChat:Profile=product"], TestContext.Current.CancellationToken);
        foreach (var module in ToolingModules) { Assert.Contains(module, names); }
        foreach (var module in UserPathModules) { Assert.Contains(module, names); }
    }

    private static async Task<string[]> ResourceNames(string[] args, CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(args, cancellationToken);
        return appHost.Resources.Select(resource => resource.Name).ToArray();
    }
}