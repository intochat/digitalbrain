using System.Net;
using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Google.Gmail;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GmailOAuthCallbackFacts
{
    [Fact(Timeout = 180_000)]
    public async Task AuthorizationCodeCallbackPublishesGmailConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var stub = await TokenEndpointStub.StartAsync(ct);
        await using var brain = await StartAsync(stub, ct, new Dictionary<string, string?>
        {
            ["DigitalBrain:Integrations:gmail:ClientId"] = "integration-client",
            ["DigitalBrain:Integrations:gmail:ClientSecret"] = "integration-secret",
            ["DigitalBrain:Integrations:gmail:PublicOrigin"] = stub.Origin.AbsoluteUri,
        });
        var gmail = brain.Get<IGmail>("gmail");
        await using var connected = await brain.Observe<GmailConnected>(gmail, ct);
        using var response = await brain.HttpClient.GetAsync("google/gmail/oauth/callback?code=fake-code", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gmail", (await connected.NextAsync(ct: ct)).EmailAddress);
    }

    [Fact(Timeout = 180_000)]
    public async Task ConnectingGmailAgainstAnUnconfiguredRegistrationExplainsInsteadOfCrashing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var stub = await TokenEndpointStub.StartAsync(ct);
        await using var brain = await StartAsync(stub, ct, new Dictionary<string, string?>());

        foreach (var path in new[] { "google/gmail/oauth/callback?code=fake-code", "integrations/gmail/login?request=x" })
        {
            using var response = await brain.HttpClient.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("gmail", json.RootElement.GetProperty("integration").GetString());
            Assert.Equal("Unconfigured", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(["ClientId", "ClientSecret", "PublicOrigin"],
                json.RootElement.GetProperty("missing").EnumerateArray().Select(field => field.GetString()).ToArray());
            Assert.DoesNotContain("Aspire", body, StringComparison.Ordinal);
        }
    }

    private static Task<E2EBrain> StartAsync(TokenEndpointStub stub, CancellationToken ct, Dictionary<string, string?> seeded)
        => E2ETest.Create()
            .WithModule<DigitalBrain.Sdk.Secrets.SecretsModule>()
            .WithModule<DigitalBrain.Sdk.Integrations.IntegrationsModule>()
            .WithModule<GmailModule, GmailModuleOptions>(options => options.TokenEndpoint = stub.TokenEndpoint)
            .WithExecution(new() { PrivateConfiguration = seeded })
            .StartAsync(ct);
}

internal sealed class TokenEndpointStub(WebApplication app, Uri origin, Uri tokenEndpoint) : IAsyncDisposable
{
    public Uri Origin => origin;
    public Uri TokenEndpoint => tokenEndpoint;

    public static async Task<TokenEndpointStub> StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/token", () => Results.Json(new
        {
            token_type = "Bearer",
            access_token = "integration-access-token",
            refresh_token = "integration-refresh-token",
            scope = "https://www.googleapis.com/auth/gmail.readonly",
            expires_in = 3600,
        }));
        await app.StartAsync(cancellationToken);
        var origin = new Uri(app.Urls.Single() + "/");
        return new TokenEndpointStub(app, origin, new Uri(origin, "token"));
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
