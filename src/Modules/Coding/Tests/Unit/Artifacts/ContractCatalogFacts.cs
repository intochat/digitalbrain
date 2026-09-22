using System.Text.Json;
using DigitalBrain.Coding;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ContractCatalogFacts
{
    [Fact]
    public void CatalogFitsTheAuthoringInstructionBudget()
    {
        var paths = Directory.GetFiles(AppContext.BaseDirectory, "DigitalBrain*.dll");
        var catalog = new ContractCatalog(Options.Create(new CodeExecutionOptions { ReferencePaths = paths }));
        var snapshot = catalog.Read([]);
        Assert.NotEmpty(snapshot.Contracts);
        Assert.True(JsonSerializer.Serialize(snapshot).Length <= 24000);
        Assert.NotEmpty(snapshot.EnvironmentHash);
    }
}
