using DigitalBrain.Core;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Registry;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Microsoft.CSharp.Tests;

public sealed class ContractDiscoveryFacts
{
    [Fact]
    public async Task DiscoveryListsComposedModulesAndReadsTheirContractsFromTheRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CSharpModule>().WithModule<RegistryModule>().WithReminders()
            .StartAsync(ct);
        var discovery = brain.SiloServices.GetRequiredService<CSharpContractDiscovery>();

        var index = await discovery.Read([], ct);
        Assert.Contains(index.Modules, module => module.Id == "csharp");
        Assert.Contains(index.Modules, module => module.Id == "registry");
        Assert.Empty(index.Contracts);

        var csharp = await discovery.Read(["csharp"], ct);
        Assert.Contains(csharp.Contracts, line => line.Contains(typeof(ICSharpFile).FullName!, StringComparison.Ordinal));
        Assert.DoesNotContain(csharp.Contracts, line => line.Contains(typeof(IRegistry).FullName!, StringComparison.Ordinal));

        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => discovery.Read(["nope"], ct));
        Assert.Contains("Installed module IDs", unknown.Message, StringComparison.Ordinal);
    }
}
