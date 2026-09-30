using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Tests;

public sealed class RoslynSharedOptionsFacts
{
    [Fact]
    public void RoslynReadsTheSolutionAndWorkspaceKeyCodingDeclares()
    {
        var definition = ModuleOptionsSerialization.Compile<CodingModule, CodingModuleOptions>(
            new CodingModuleOptions { SolutionPath = "C:/work/app.slnx", WorkspaceKey = "shared-workspace" });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();

        var roslyn = new RoslynModuleOptions();
        RoslynModuleOptions.ReadFromCodingOptions(roslyn, configuration);

        Assert.Equal("C:/work/app.slnx", roslyn.SolutionPath);
        Assert.Equal("shared-workspace", roslyn.WorkspaceKey);
    }
}
