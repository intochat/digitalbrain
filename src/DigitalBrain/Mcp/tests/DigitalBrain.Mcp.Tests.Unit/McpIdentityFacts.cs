using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Mcp.Tests.Unit;

public sealed class McpIdentityFacts
{
    [Fact]
    public async Task OpenMcpStampsTheOwnerAndRequestedBrainForEachToolCall()
    {
        await using var app = await Start("Open");
        using var client = Client(app);
        foreach (var brain in new[] { "first", "second", "first" })
        {
            using var response = await Call(client, $"/brains/{brain}/mcp");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var data = body.Split('\n').Single(line => line.StartsWith("data: "))[6..];
            using var result = JsonDocument.Parse(data);
            using var caller = JsonDocument.Parse(result.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
            Assert.Equal("owner", caller.RootElement.GetProperty("principalId").GetString());
            Assert.Equal(brain, caller.RootElement.GetProperty("brainId").GetString());
        }
    }

    [Fact]
    public async Task SecuredMcpRejectsAnonymousRequests()
    {
        await using var app = await Start("Secured");
        using var client = Client(app);
        using var response = await Call(client, "/brains/first/mcp");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecuredMcpUsesTheExistingBasicIdentityWithoutAcceptingCallerHeaders()
    {
        await using var app = await Start("Secured", credentials: true);
        using var client = Client(app);
        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("operator:test-password")));
        client.DefaultRequestHeaders.Add("X-Brain-Id", "forged");
        using var response = await Call(client, "/brains/selected/mcp");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var result = JsonDocument.Parse(body.Split('\n').Single(line => line.StartsWith("data: "))[6..]);
        using var caller = JsonDocument.Parse(result.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal("operator", caller.RootElement.GetProperty("principalId").GetString());
        Assert.Equal("operator", caller.RootElement.GetProperty("accountId").GetString());
        Assert.Equal("selected", caller.RootElement.GetProperty("brainId").GetString());
    }

    [Fact]
    public async Task McpWithoutAnExplicitPostureRefusesToStart()
        => await Assert.ThrowsAsync<OptionsValidationException>(() => Start(null));

    [Fact]
    public async Task MembershipAndBrainIdValidationApplyBeforeToolDispatch()
    {
        await using var app = await Start("Open", deny: true);
        using var client = Client(app);
        using var denied = await Call(client, "/brains/other/mcp");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalid = await Call(client, "/brains/" + new string('x', 201) + "/mcp");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<WebApplication> Start(string? posture, bool deny = false, bool credentials = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["DigitalBrain:Auth:Posture"] = posture;
        if (credentials)
        {
            builder.Configuration["DigitalBrain:Auth:Username"] = "operator";
            builder.Configuration["DigitalBrain:Auth:Password"] = "test-password";
        }
        builder.Services.AddDigitalBrainMcp();
        builder.Services.AddSingleton<IBrainAccess>(new Access(!deny));
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseRouting();
        app.UseAuthentication();
        app.UseAccountSession();
        app.MapDigitalBrainMcp();
        try { await app.StartAsync(TestContext.Current.CancellationToken); }
        catch { await app.DisposeAsync(); throw; }
        return app;
    }

    private static HttpClient Client(WebApplication app)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        return client;
    }

    private static Task<HttpResponseMessage> Call(HttpClient client, string path) => client.PostAsJsonAsync(path,
        new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "neurons_context", arguments = new { } } },
        TestContext.Current.CancellationToken);

    private sealed class Access(bool allow) : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string accountId, string brainId, CancellationToken cancellationToken = default)
            => new(allow);
    }
}
