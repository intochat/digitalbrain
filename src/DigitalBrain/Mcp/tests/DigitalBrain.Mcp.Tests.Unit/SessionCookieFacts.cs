using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform;
using DigitalBrain.Platform.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Mcp.Tests.Unit;

public sealed class SessionCookieFacts
{
    [Fact]
    public async Task RuntimeIssuedCookieAuthenticatesOnASeparateMcpHost()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pr131-cookie-" + Guid.NewGuid().ToString("N")));
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().StartAsync(ct);
        await using var runtime = await Host(false, keys, brain);
        await using var mcp = await Host(true, keys, brain);
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false });
        using var registration = await client.PostAsJsonAsync(runtime.Urls.Single() + "/identity/register",
            new { principalId = "alice", password = "correct-password", displayName = "Alice" }, ct);
        registration.EnsureSuccessStatusCode();
        var cookie = registration.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("intochat.session=", StringComparison.Ordinal)).Split(';')[0];
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        using var response = await client.PostAsJsonAsync(mcp.Urls.Single() + "/mcp",
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "neurons_context", arguments = new { } } }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alice", await response.Content.ReadAsStringAsync(ct));
        using var denied = await client.PostAsJsonAsync(mcp.Urls.Single() + "/brains/foreign/mcp",
            new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "neurons_context", arguments = new { } } }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Membership in the selected route is insufficient for a raw foreign key.
        foreach (var (contract, key, expectedError) in new[]
        {
            (typeof(IApp).FullName!, BrainScope.Create("someone", "else").Id + "/packages/alice/example", true),
            (typeof(IPackage).FullName!, "alice/example", false),
        })
        {
            using var invocation = await client.PostAsJsonAsync(mcp.Urls.Single() + "/mcp",
                new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "tools/call",
                    @params = new
                    {
                        name = "neurons_call",
                        arguments = new { contract, key, method = "Read", arguments = "[]" }
                    }
                }, ct);
            invocation.EnsureSuccessStatusCode();
            var body = await invocation.Content.ReadAsStringAsync(ct);
            using var result = JsonDocument.Parse(body.Split('\n').Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..]);
            Assert.Equal(expectedError, result.RootElement.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean());
        }
    }

    private static async Task<WebApplication> Host(bool mcp, DirectoryInfo keys, ModuleBrain brain)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = mcp ? "DigitalBrain.Mcp" : "IntoChat" });
        builder.Configuration["DigitalBrain:Auth:Posture"] = "Secured";
        builder.Configuration["DigitalBrain:Identity:CookieName"] = "intochat.session";
        builder.Configuration["DigitalBrain:Identity:ProtectionApplicationName"] = "IntoChat.v1";
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(brain.Grains);
        if (mcp) { builder.Services.AddDigitalBrainMcp(); }
        else { builder.Services.AddIdentity(); }
        builder.Services.AddDataProtection().PersistKeysToFileSystem(keys);
        // The runtime's protection identity is currently registered separately from AddIdentity.
        if (!mcp) { builder.Services.AddDataProtection().SetApplicationName("IntoChat.v1"); }
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseRouting();
        app.UseAuthentication();
        app.UseAccountSession();
        if (mcp) { app.MapDigitalBrainMcp(); }
        else { app.MapDigitalBrainPlatform(); }
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
