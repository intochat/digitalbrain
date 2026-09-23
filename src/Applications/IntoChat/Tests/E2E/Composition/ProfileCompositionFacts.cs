using Aspire.Hosting.Testing;

namespace IntoChat.Tests.E2E.Composition;

/// <summary>
/// P0.3 profile guard. The AppHost composes developer-only modules (Aspire project path, Roslyn,
/// DotNet, Coding, Behavior) only in the developer profile; the product profile keeps user-path
/// modules. This builds the real AppHost model without starting any resource.
/// </summary>
public sealed class ProfileCompositionFacts
{
    private static readonly string[] DeveloperOnlyModules = ["Aspire", "Roslyn", "DotNet", "Coding", "Behavior"];

    private static readonly string[] UserPathModules =
        ["AI", "Memory", "ClickHouse", "Supabase", "Time", "Gmail", "Salesforce", "GitHub", "Flutter"];

    [Fact]
    public async Task DeveloperProfileComposesDeveloperOnlyModules()
    {
        var names = await ResourceNames(["IntoChat:Profile=developer"], TestContext.Current.CancellationToken);
        foreach (var module in DeveloperOnlyModules) { Assert.Contains(module, names); }
        foreach (var module in UserPathModules) { Assert.Contains(module, names); }
    }

    [Fact]
    public async Task ProductProfileComposesOnlyUserPathModules()
    {
        var names = await ResourceNames(["IntoChat:Profile=product"], TestContext.Current.CancellationToken);
        foreach (var module in DeveloperOnlyModules) { Assert.DoesNotContain(module, names); }
        foreach (var module in UserPathModules) { Assert.Contains(module, names); }
    }

    private static async Task<string[]> ResourceNames(string[] args, CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(args, cancellationToken);
        return appHost.Resources.Select(resource => resource.Name).ToArray();
    }
}