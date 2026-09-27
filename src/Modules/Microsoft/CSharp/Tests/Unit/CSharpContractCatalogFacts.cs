using DigitalBrain.Microsoft.CSharp;
using ITimer = DigitalBrain.Time.Timers.ITimer;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace DigitalBrain.Tests;

public sealed class CSharpContractCatalogFacts
{
    [Fact]
    public void ListsInstalledContractsWithTheirProjectDirective()
    {
        var catalog = new CSharpContractCatalog(Create(new CSharpOptions { SourceRoot = RepositoryRoot.Find() }));

        var snapshot = catalog.Read(["time"]);

        var time = Assert.Single(snapshot.Modules, module => module.Id == "time");
        Assert.Equal("#:project /brain/src/Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj", time.Directive);
        Assert.Contains(snapshot.Contracts, contract => contract.StartsWith("DigitalBrain.Time.Timers.ITimer {", StringComparison.Ordinal));
        Assert.Contains("DigitalBrainClient.ConnectAsync", snapshot.Example, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownModulesWithTheInstalledList()
    {
        var catalog = new CSharpContractCatalog(Create(new CSharpOptions()));

        var error = Assert.Throws<ArgumentException>(() => catalog.Read(["nope"]));

        Assert.Contains("microsoft.csharp", error.Message, StringComparison.Ordinal);
    }
}
