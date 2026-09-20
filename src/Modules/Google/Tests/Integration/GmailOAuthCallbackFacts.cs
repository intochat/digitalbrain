using System.Net;
using DigitalBrain.Core;
using DigitalBrain.Google;
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
        var options = new GoogleModuleOptions
        {
            PublicOrigin = stub.Origin,
            TokenEndpoint = stub.TokenEndpoint,
        };
        await using var brain = await IntegrationTest.Create().WithModule<GoogleModule>(google => google.WithOptions(options))
            .WithExecution(new()
            {
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    [GoogleModule.GmailOAuthConfigurationRoot + ":ClientId"] = "integration-client",
                    [GoogleModule.GmailOAuthConfigurationRoot + ":ClientSecret"] = "integration-secret",
                },
            }).StartAsync(ct);
        var gmail = brain.Get<IGmail>("gmail");
        await using var connected = await brain.Observe<GmailConnected>(gmail, ct);
        using var response = await brain.HttpClient.GetAsync("google/gmail/oauth/callback?code=fake-code", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("gmail", (await connected.NextAsync(ct: ct)).EmailAddress);
    }
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
