using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AuthoringAvailabilityFacts
{
    [Theory]
    [InlineData("prompt", false, false)]
    [InlineData("prompt", true, true)]
    [InlineData("csharp", false, true)]
    public async Task OnlyRevisionsThatRunScriptsNeedASandbox(string runtime, bool hasTests, bool requiresSandbox)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        var id = PackageId.Create("alice", "availability");
        var files = new Dictionary<string, string>();
        if (hasTests) { files[PackageContent.TestsPath] = "Console.WriteLine(1);"; }
        var content = new PackageContent(new("Availability", "Checks sandbox policy", [new("ask", "Answer")], [], Runtime: runtime),
            runtime == "csharp" ? "Console.WriteLine(1);" : "", files);
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(new(Guid.NewGuid(), null, content, "Check availability"));
        var service = brain.SiloServices.GetRequiredService<MarketplaceService>();
        if (requiresSandbox)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequireRunnable(new(id, revision.Id)));
        }
        else { await service.RequireRunnable(new(id, revision.Id)); }
        Assert.Equal(runtime, (await service.Spec(id, revision.Id)).Runtime);
    }
}
