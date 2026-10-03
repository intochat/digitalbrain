using System.Net;
using System.Net.Http.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Platform.Identity.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class AuthPostureFacts
{
    [Fact]
    public async Task A_host_that_declares_no_posture_refuses_to_start_with_the_configuration_key_in_the_message()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddIdentity();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        var refusal = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(ct));
        Assert.Contains(AuthOptions.PostureKey, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bootstrap_credential_with_only_a_username_refuses_to_start()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[AuthOptions.PostureKey] = "Secured";
        builder.Configuration["DigitalBrain:Auth:Username"] = "ops";
        builder.Services.AddIdentity();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(ct));
    }

    [Fact]
    public async Task The_secured_posture_answers_401_to_an_anonymous_request_even_without_a_bootstrap_credential()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartGate("Secured", ct);
        using var client = Client(app);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/probe", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", ct)).StatusCode);
    }

    [Fact]
    public async Task The_open_posture_stamps_the_synthetic_owner_on_an_anonymous_request()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var app = await StartGate("Open", ct);
        using var client = Client(app);
        var response = await client.GetAsync("/probe", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AccountSession.DefaultLogin, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Registering_the_reserved_bootstrap_username_conflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithExecution(new DigitalBrain.Testing.TestExecutionOptions
        {
            PrivateConfiguration = new Dictionary<string, string?>
            {
                [AuthOptions.PostureKey] = "Secured",
                ["DigitalBrain:Auth:Username"] = "ops",
                ["DigitalBrain:Auth:Password"] = "a-long-operator-password",
            },
        }).StartAsync(ct);
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[AuthOptions.PostureKey] = "Secured";
        builder.Configuration["DigitalBrain:Auth:Username"] = "ops";
        builder.Configuration["DigitalBrain:Auth:Password"] = "a-long-operator-password";
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddIdentity();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UsePlatformHttp();
        IdentityEndpoints.Map(app);
        await app.StartAsync(ct);
        using var client = Client(app);
        using var reserved = await client.PostAsJsonAsync("/identity/register", new { principalId = "OPS", password = "whatever" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, reserved.StatusCode);
        using var allowed = await client.PostAsJsonAsync("/identity/register", new { principalId = "someone", password = "a-strong-password" }, ct);
        allowed.EnsureSuccessStatusCode();
        await app.StopAsync(ct);
    }

    private static async Task<WebApplication> StartGate(string posture, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[AuthOptions.PostureKey] = posture;
        builder.Services.AddIdentity();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UsePlatformHttp();
        app.MapGet("/health", static () => Results.Ok());
        app.MapGet("/probe", static () => Results.Text(
            DigitalBrain.Kernel.Enforcement.CallerContextStamper.TryGet(out var caller) ? caller.PrincipalId : ""));
        await app.StartAsync(ct);
        return app;
    }

    private static HttpClient Client(WebApplication app)
        => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(app.Urls.Single()) };
}
