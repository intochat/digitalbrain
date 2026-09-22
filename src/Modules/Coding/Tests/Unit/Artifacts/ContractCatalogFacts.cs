using System.Text.Json;
using DigitalBrain.Coding;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ContractCatalogFacts
{
    [Fact]
    public void BootstrapExampleCompilesAgainstInstalledContracts()
    {
        using var host = new HostBuilder().UseOrleans(silo => new CodingModule().Configure(silo)).Build();
        var options = host.Services.GetRequiredService<IOptions<CodeExecutionOptions>>().Value;
        var source = host.Services.GetRequiredService<ContractCatalog>().Read(["time"]).Example
            .Replace("MySignal", "DigitalBrain.Time.Timers.Signals.TimerTick", StringComparison.Ordinal)
            .Replace("IMyNeuron", "DigitalBrain.Time.Timers.ITimer", StringComparison.Ordinal);
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(options.ReferencePaths).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("CatalogExample", [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            paths.Select(path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void InstalledContractsAreDiscoverableWhenExecutionIsDisabled()
    {
        using var host = new HostBuilder().UseOrleans(silo => new CodingModule().Configure(silo)).Build();
        Assert.Null(host.Services.GetRequiredService<IOptions<CodeExecutionOptions>>().Value.Root);
        var snapshot = host.Services.GetRequiredService<ContractCatalog>().Read(["time", "flutter"]);
        Assert.Contains(snapshot.Contracts, contract => contract.Contains("TimerTick", StringComparison.Ordinal));
        Assert.Contains(snapshot.Contracts, contract => contract.Contains("TimerTick(String TimerId, DateTimeOffset ObservedAt)", StringComparison.Ordinal));
        Assert.Contains(snapshot.Contracts, contract => contract.Contains("IText", StringComparison.Ordinal));
        Assert.Contains(snapshot.Contracts, contract => contract.Contains("IDigitalBrain", StringComparison.Ordinal) && contract.Contains("SubscribeAsync", StringComparison.Ordinal));
        Assert.Contains(snapshot.Contracts, contract => contract.Contains("ISignalSubscription", StringComparison.Ordinal) && contract.Contains("ReadAllAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownModuleReportsInstalledChoicesForRepair()
    {
        var catalog = new ContractCatalog(Options.Create(new CodeExecutionOptions { Modules = new() { ["time"] = [], ["flutter"] = [] } }));
        var error = Assert.Throws<ArgumentException>(() => catalog.Read(["timer-status"]));
        Assert.Contains("flutter", error.Message, StringComparison.Ordinal);
        Assert.Contains("time", error.Message, StringComparison.Ordinal);
    }

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
