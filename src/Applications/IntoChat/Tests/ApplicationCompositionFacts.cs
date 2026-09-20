using Aspire.Hosting.Testing;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace IntoChat.Tests;

public sealed class ApplicationCompositionFacts
{
    [Fact]
    public async Task TestRuntimeDoesNotInheritDevelopmentPorts()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(
            ["DigitalBrain:Testing:Enabled=true"], TestContext.Current.CancellationToken);
        var runtime = Assert.Single(builder.Resources, resource => resource.Annotations.OfType<BrainEndpointAnnotation>().Any());
        var http = Assert.Single(runtime.Annotations.OfType<EndpointAnnotation>(), endpoint => endpoint.Name == "http");
        Assert.Null(http.Port);
        Assert.Null(http.TargetPort);
    }

    [Fact(Timeout = 240_000)]
    public async Task ConcurrentDeploymentsHaveIndependentRuntimePorts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var firstDeployment = await IntoChatTestDeployment.CreateAsync(ct);
        await using var secondDeployment = await IntoChatTestDeployment.CreateAsync(ct);
        await using var first = await firstDeployment.CreateTest().StartAsync(ct);
        await using var second = await secondDeployment.CreateTest().StartAsync(ct);
        Assert.NotEqual(first.HttpClient.BaseAddress!.Port, second.HttpClient.BaseAddress!.Port);
        Assert.NotEqual(5310, first.HttpClient.BaseAddress.Port);
        Assert.NotEqual(5310, second.HttpClient.BaseAddress.Port);
    }

    [Fact]
    public void ExcelAndFlutterUseDistinctSerializedSheetIdentities()
    {
        var excel = typeof(DigitalBrain.Excel.Spreadsheet.ISpreadsheet).GetCustomAttributes(typeof(Orleans.AliasAttribute), false).Cast<Orleans.AliasAttribute>().Single();
        var flutter = typeof(DigitalBrain.Flutter.Sheet.ISheet).GetCustomAttributes(typeof(Orleans.AliasAttribute), false).Cast<Orleans.AliasAttribute>().Single();
        Assert.NotEqual(excel.Alias, flutter.Alias);
    }

    [Fact]
    public async Task ApplicationExplicitlyDeclaresEveryProductionModule()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(
            ["DigitalBrain:Testing:Enabled=true"], TestContext.Current.CancellationToken);
        var modules = builder.Resources.SelectMany(r => r.Annotations.OfType<BrainModuleAnnotation>())
            .Select(a => a.ModuleType.Name.Replace("Module", "", StringComparison.Ordinal)).ToArray();
        foreach (var name in new[] { "ai", "memory", "clickhouse", "supabase", "time", "excel", "google", "salesforce", "microsoft", "coding", "flutter" })
            { Assert.Contains(name, modules, StringComparer.OrdinalIgnoreCase); }
        Assert.Contains(builder.Resources, r => r.Name == "FlutterShell");
        Assert.Equal(12, modules.Length);
        Assert.Contains("TestTwitter", modules);
    }
}
