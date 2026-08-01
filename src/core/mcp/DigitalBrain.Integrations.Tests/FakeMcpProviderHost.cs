using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DigitalBrain.Integrations.Tests;

internal sealed class FakeMcpProviderHost : IAsyncDisposable
{
    internal const string ClientId = "digitalbrain-test-client";
    internal const string ClientSecret = "digitalbrain-test-secret";
    internal const string AccessTokenPrefix = "fake-access-";
    internal const string ShortLivedMarker = "short";

    private static readonly string[] ResponseTypesSupported = ["code"];
    private static readonly string[] GrantTypesSupported = ["authorization_code", "refresh_token"];
    private static readonly string[] CodeChallengeMethodsSupported = ["S256"];
    private static readonly string[] TokenEndpointAuthMethodsSupported =
    [
        "client_secret_post",
        "client_secret_basic",
        "none",
    ];
    private static readonly string[] GmailScopesSupported =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
    ];
    private static readonly string[] SalesforceScopesSupported =
    [
        "mcp_api",
        "refresh_token",
    ];

    private readonly ConcurrentDictionary<string, PendingCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IssuedToken> _tokens = new(StringComparer.Ordinal);
    private readonly string[] _scopesSupported;
    private readonly Func<FakeMcpProviderHost, McpServerTool> _toolFactory;
    private readonly WebApplication _app;
    private int _bearerHits;

    private FakeMcpProviderHost(
        WebApplication app,
        Uri baseAddress,
        string[] scopesSupported,
        Func<FakeMcpProviderHost, McpServerTool> toolFactory,
        string sampleMessageId,
        string sampleSubject,
        string sampleSender,
        string sampleBody,
        string sampleAccountId,
        string sampleDescription)
    {
        _app = app;
        _scopesSupported = scopesSupported;
        _toolFactory = toolFactory;
        BaseAddress = baseAddress;
        AuthorizeEndpoint = new Uri(baseAddress, "/authorize");
        TokenEndpoint = new Uri(baseAddress, "/token");
        McpEndpoint = baseAddress;
        SampleMessageId = sampleMessageId;
        SampleSubject = sampleSubject;
        SampleSender = sampleSender;
        SampleBody = sampleBody;
        SampleAccountId = sampleAccountId;
        SampleDescription = sampleDescription;
    }

    internal Uri BaseAddress { get; }
    internal Uri AuthorizeEndpoint { get; }
    internal Uri TokenEndpoint { get; }
    internal Uri McpEndpoint { get; }
    internal string SampleMessageId { get; }
    internal string SampleSubject { get; }
    internal string SampleSender { get; }
    internal string SampleBody { get; }
    internal string SampleAccountId { get; }
    internal string SampleDescription { get; }
    internal bool DenyNextAuthorization { get; set; }
    internal int BearerHits => Volatile.Read(ref _bearerHits);
    internal string? LastBearerToken { get; private set; }

    internal static Task<FakeMcpProviderHost> StartAsync(
        string sampleMessageId,
        string sampleSubject,
        string sampleSender,
        string sampleBody,
        CancellationToken cancellationToken)
        => StartCoreAsync(
            GmailScopesSupported,
            CreateAdmittedGetMessage,
            sampleMessageId,
            sampleSubject,
            sampleSender,
            sampleBody,
            sampleAccountId: string.Empty,
            sampleDescription: string.Empty,
            cancellationToken);

    internal static Task<FakeMcpProviderHost> StartForSalesforceAsync(
        string sampleAccountId,
        string sampleDescription,
        CancellationToken cancellationToken)
        => StartCoreAsync(
            SalesforceScopesSupported,
            host => AdmittedMcpTools.SalesforceSoqlQuery(host.SampleAccountId, host.SampleDescription),
            sampleMessageId: string.Empty,
            sampleSubject: string.Empty,
            sampleSender: string.Empty,
            sampleBody: string.Empty,
            sampleAccountId,
            sampleDescription,
            cancellationToken);

    private static async Task<FakeMcpProviderHost> StartCoreAsync(
        string[] scopesSupported,
        Func<FakeMcpProviderHost, McpServerTool> toolFactory,
        string sampleMessageId,
        string sampleSubject,
        string sampleSender,
        string sampleBody,
        string sampleAccountId,
        string sampleDescription,
        CancellationToken cancellationToken)
    {
        var port = FreePort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(baseAddress.AbsoluteUri);
        builder.Logging.ClearProviders();

        // Late-bound host so auth/tool handlers can close over the instance created after Build().
        FakeMcpProviderHost? host = null;

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Bearer";
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, OpaqueBearerHandler>("Bearer", _ => { })
            .AddMcp(options =>
            {
                options.ResourceMetadata = new()
                {
                    Resource = baseAddress.AbsoluteUri,
                    AuthorizationServers = { baseAddress.AbsoluteUri },
                    ScopesSupported = [.. scopesSupported],
                    BearerMethodsSupported = ["header"],
                };
            });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IOpaqueTokenValidator>(
            new DelegateTokenValidator(token => host?.TryValidateBearer(token) == true));
        McpServerTool? tool = null;
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithListToolsHandler((_, _) =>
            {
                tool ??= toolFactory(host!);
                return ValueTask.FromResult(new ListToolsResult { Tools = [tool.ProtocolTool] });
            })
            .WithCallToolHandler(async (request, cancellation) =>
            {
                tool ??= toolFactory(host!);
                return await tool.InvokeAsync(request, cancellation);
            });

        var app = builder.Build();
        host = new FakeMcpProviderHost(
            app,
            baseAddress,
            scopesSupported,
            toolFactory,
            sampleMessageId,
            sampleSubject,
            sampleSender,
            sampleBody,
            sampleAccountId,
            sampleDescription);

        app.UseAuthentication();
        app.UseAuthorization();
        MapOAuth(app, host);
        app.MapMcp().RequireAuthorization();
        await app.StartAsync(cancellationToken);
        return host;
    }

    private static void MapOAuth(WebApplication app, FakeMcpProviderHost host)
    {
        app.MapGet("/.well-known/oauth-authorization-server", (HttpContext http) =>
        {
            var issuer = $"{http.Request.Scheme}://{http.Request.Host}/";
            return Results.Json(new
            {
                issuer,
                authorization_endpoint = $"{issuer}authorize",
                token_endpoint = $"{issuer}token",
                response_types_supported = ResponseTypesSupported,
                grant_types_supported = GrantTypesSupported,
                code_challenge_methods_supported = CodeChallengeMethodsSupported,
                token_endpoint_auth_methods_supported = TokenEndpointAuthMethodsSupported,
                scopes_supported = host._scopesSupported,
            });
        });

        app.MapGet("/authorize", (HttpRequest request) =>
        {
            var state = request.Query["state"].ToString();
            var redirectUri = request.Query["redirect_uri"].ToString();
            var clientId = request.Query["client_id"].ToString();
            var challenge = request.Query["code_challenge"].ToString();
            var method = request.Query["code_challenge_method"].ToString();
            var lifetime = request.Query["token_lifetime"].ToString();
            if (string.IsNullOrWhiteSpace(state)
                || string.IsNullOrWhiteSpace(redirectUri)
                || string.IsNullOrWhiteSpace(clientId)
                || string.IsNullOrWhiteSpace(challenge))
            {
                return Results.BadRequest("missing OAuth authorize parameters");
            }

            if (!string.IsNullOrWhiteSpace(method) && !string.Equals(method, "S256", StringComparison.Ordinal))
            {
                return Results.BadRequest("only S256 PKCE is supported");
            }

            if (host.DenyNextAuthorization
                || string.Equals(request.Query["error"], "access_denied", StringComparison.Ordinal))
            {
                host.DenyNextAuthorization = false;
                return Results.Redirect(AppendQuery(
                    redirectUri,
                    $"error=access_denied&state={Uri.EscapeDataString(state)}"));
            }

            var code = Guid.NewGuid().ToString("N");
            var shortLived = string.Equals(lifetime, ShortLivedMarker, StringComparison.OrdinalIgnoreCase);
            host._codes[code] = new PendingCode(clientId, redirectUri, challenge, shortLived);

            var issuer = $"{request.Scheme}://{request.Host}/";
            return Results.Redirect(AppendQuery(
                redirectUri,
                $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}&iss={Uri.EscapeDataString(issuer)}"));
        });

        app.MapPost("/token", async (HttpRequest request) =>
        {
            var form = await request.ReadFormAsync();
            if (!string.Equals(form["grant_type"], "authorization_code", StringComparison.Ordinal))
            {
                return Results.BadRequest(new { error = "unsupported_grant_type" });
            }

            var code = form["code"].ToString();
            var verifier = form["code_verifier"].ToString();
            var redirectUri = form["redirect_uri"].ToString();
            var clientId = form["client_id"].ToString();
            if (string.IsNullOrWhiteSpace(clientId)
                && AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
                && string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                && header.Parameter is { Length: > 0 } parameter)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));
                clientId = Uri.UnescapeDataString(decoded.Split(':', 2)[0]);
            }

            if (!host._codes.TryRemove(code, out var pending))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.Equals(pending.ClientId, clientId, StringComparison.Ordinal)
                || !string.Equals(pending.RedirectUri, redirectUri, StringComparison.Ordinal)
                || !VerifyPkce(pending.CodeChallenge, verifier))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var expiresIn = pending.ShortLived
                || string.Equals(form["token_lifetime"], ShortLivedMarker, StringComparison.OrdinalIgnoreCase)
                ? 1
                : 3600;
            var accessToken = AccessTokenPrefix + Guid.NewGuid().ToString("N");
            host._tokens[accessToken] = new IssuedToken(DateTimeOffset.UtcNow.AddSeconds(expiresIn + 30));
            return Results.Json(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = expiresIn,
            });
        });
    }

    internal bool TryValidateBearer(string token)
    {
        if (!_tokens.TryGetValue(token, out var issued) || issued.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        Interlocked.Increment(ref _bearerHits);
        LastBearerToken = token;
        return true;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static McpServerTool CreateAdmittedGetMessage(FakeMcpProviderHost host)
        => AdmittedMcpTools.GmailGetMessage(
            host.SampleMessageId,
            host.SampleSubject,
            host.SampleSender,
            host.SampleBody);

    private static string AppendQuery(string uri, string query)
        => uri.Contains('?', StringComparison.Ordinal) ? $"{uri}&{query}" : $"{uri}?{query}";

    private static bool VerifyPkce(string challenge, string verifier)
    {
        if (string.IsNullOrWhiteSpace(verifier))
        {
            return false;
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var computed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return string.Equals(computed, challenge, StringComparison.Ordinal);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record PendingCode(string ClientId, string RedirectUri, string CodeChallenge, bool ShortLived);
    private sealed record IssuedToken(DateTimeOffset ExpiresAt);
}

internal interface IOpaqueTokenValidator
{
    bool Validate(string token);
}

internal sealed class DelegateTokenValidator(Func<string, bool> validate) : IOpaqueTokenValidator
{
    public bool Validate(string token) => validate(token);
}

internal sealed class OpaqueBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOpaqueTokenValidator tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        if (!tokens.Validate(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("invalid bearer token"));
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.Name, "fake-mcp-user"));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
