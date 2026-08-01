using System.Net;

namespace DigitalBrain.Google.Tests.Auth;

internal sealed class FakeGoogleTokenHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeGoogleTokenHost(WebApplication app, string tokenServerUrl)
    {
        _app = app;
        TokenServerUrl = tokenServerUrl;
    }

    public string TokenServerUrl { get; }

    public object? ExchangeResponse { get; set; }

    public object? RefreshResponse { get; set; }

    public HttpStatusCode ExchangeStatusCode { get; set; } = HttpStatusCode.OK;

    public object? ExchangeError { get; set; }

    public int RefreshCount { get; private set; }

    public int ExchangeCount { get; private set; }

    public static async Task<FakeGoogleTokenHost> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        FakeGoogleTokenHost? host = null;
        app.MapPost("/token", async (HttpRequest request) =>
        {
            if (host is null)
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }

            var form = await request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            if (string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
            {
                host.ExchangeCount++;
                if (host.ExchangeStatusCode != HttpStatusCode.OK)
                {
                    return Results.Json(
                        host.ExchangeError ?? new { error = "invalid_grant" },
                        statusCode: (int)host.ExchangeStatusCode);
                }

                return Results.Json(host.ExchangeResponse ?? new { error = "server_misconfigured" });
            }

            if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
            {
                host.RefreshCount++;
                return Results.Json(host.RefreshResponse ?? new { error = "server_misconfigured" });
            }

            return Results.Json(new { error = "unsupported_grant_type" }, statusCode: StatusCodes.Status400BadRequest);
        });

        await app.StartAsync();
        var address = app.Urls.Single();
        host = new FakeGoogleTokenHost(app, $"{address.TrimEnd('/')}/token");
        return host;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
