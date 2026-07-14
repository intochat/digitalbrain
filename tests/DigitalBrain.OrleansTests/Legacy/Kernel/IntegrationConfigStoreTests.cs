using System.Text;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Kernel.Contracts.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Kernel;

public class IntegrationConfigStoreTests
{
    private static (IIntegrationConfigStore store, InMemoryIntegrationConfigBackingStore backing) BuildInMemory()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var backing = new InMemoryIntegrationConfigBackingStore();
        services.AddSingleton<IIntegrationConfigBackingStore>(backing);
        services.AddSingleton<IIntegrationConfigStore, IntegrationConfigStore>();

        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IIntegrationConfigStore>(), backing);
    }

    [Fact]
    public async Task RoundTrip_Returns_Exact_Values()
    {
        var (store, _) = BuildInMemory();

        await store.SetAsync("s", "telegram", new Dictionary<string, string> { ["token"] = "abc" });
        var result = await store.GetAsync("s", "telegram");

        Assert.Equal("abc", result["token"]);
    }

    [Fact]
    public async Task Persisted_Bytes_Do_Not_Contain_Plaintext()
    {
        var (store, backing) = BuildInMemory();

        await store.SetAsync("s", "telegram", new Dictionary<string, string> { ["token"] = "abc" });

        var raw = backing.Peek("s", "telegram")!;
        var rawText = Encoding.UTF8.GetString(raw);
        Assert.DoesNotContain("abc", rawText);
    }

    [Fact]
    public async Task GetAsync_Unknown_Pack_Returns_Empty()
    {
        var (store, _) = BuildInMemory();

        var result = await store.GetAsync("unknown-scope", "unknown-pack");

        Assert.Empty(result);
    }

    [Fact]
    public async Task Different_Scopes_Are_Isolated()
    {
        var (store, _) = BuildInMemory();

        await store.SetAsync("scope-a", "mypak", new Dictionary<string, string> { ["key"] = "value-a" });

        var fromB = await store.GetAsync("scope-b", "mypak");
        Assert.Empty(fromB);
    }

    [Fact]
    public async Task Different_Packs_Are_Isolated()
    {
        var (store, _) = BuildInMemory();

        await store.SetAsync("s", "pack-one", new Dictionary<string, string> { ["key"] = "alpha" });

        var fromTwo = await store.GetAsync("s", "pack-two");
        Assert.Empty(fromTwo);
    }
}
