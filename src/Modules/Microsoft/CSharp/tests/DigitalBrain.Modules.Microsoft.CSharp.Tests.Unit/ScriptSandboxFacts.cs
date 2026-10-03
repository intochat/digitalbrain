using DigitalBrain.Apps;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class ScriptSandboxFacts
{
    [Fact]
    public async Task TheSandboxContractDiscoversComposedContractsAndReportsCompilationErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<CSharpModule>().WithModule<RegistryModule>()
            .WithReminders().StartAsync(ct);
        var sandbox = brain.SiloServices.GetRequiredService<IScriptSandbox>();
        Assert.True(sandbox.CanRun);
        var contracts = await sandbox.ReadContracts(["csharp"], ct);
        Assert.Contains(contracts.Contracts, line => line.Contains(typeof(ICSharpFile).FullName!, StringComparison.Ordinal));
        var invalid = sandbox.Check(new Dictionary<string, string> { ["app.cs"] = "Console.WriteLine(doesNotExist);" });
        Assert.False(invalid.Success);
        Assert.Contains(invalid.Errors, error => error.Id == "CS0103" && error.File == "app.cs");
        Assert.True(sandbox.Check(new Dictionary<string, string> { ["app.cs"] = "Console.WriteLine(1);" }).Success);
    }
}
