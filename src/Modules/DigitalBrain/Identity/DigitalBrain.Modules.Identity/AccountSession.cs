using DigitalBrain.Identity.Configuration;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Identity;

// The authenticated edge. A cookie session (issued by the identity endpoints) or, for the
// single-owner bootstrap, the configured Basic credential decides the principal; when neither is
// configured the kernel stays open, which is the local and test posture. Resolving a principal
// stamps a CallerContext on the Orleans RequestContext so it travels with grain calls. Replaces
// the former Basic-only gate.
public static class AccountSession
{
    public const string DefaultLogin = "owner";
    public const string CheckPath = "/auth/check";

    private const string BasicScheme = "Basic";

    // Generous for "user:pass" while keeping the decode buffer stack-safe.
    private const int MaxEncodedCredentialChars = 1024;

    // Probed by the shell's login screen and by container probes; never gated.
    private static readonly string[] AnonymousPaths = ["/health", "/alive"];

    public static WebApplication UseAccountSession(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var credential = BasicCredential.FromOptions(ResolveAuthOptions(app));

        app.Use(async (context, next) =>
        {
            if (credential is not null && context.Request.Path == "/identity/register")
            {
                context.Request.EnableBuffering();
                try
                {
                    using var body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body);
                    if (body.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        body.RootElement.EnumerateObject().Any(property =>
                            string.Equals(property.Name, "principalId", StringComparison.OrdinalIgnoreCase) &&
                            property.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                            property.Value.GetString() == credential.Username))
                    {
                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        return;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                finally { context.Request.Body.Position = 0; }
            }

            if (IsAnonymous(context.Request))
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            var principal = ResolvePrincipal(context, credential);
            if (principal is null)
            {
                // No WWW-Authenticate: the browser's native Basic prompt would race the Flutter
                // login screen and cannot be dismissed from inside the app.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            CallerContextStamper.Stamp(principal);
            await next(context).ConfigureAwait(false);
        });

        // Inside the gate, so reaching it at all proves the session is good.
        app.MapGet(CheckPath, static () => Results.NoContent());

        return app;
    }

    // Small standalone hosts can use the middleware without registering the options; open-generic
    // IOptions registration alone does not mean auth was configured.
    private static BasicAuthOptions ResolveAuthOptions(WebApplication app)
    {
        var configured = app.Services.GetServices<IConfigureOptions<BasicAuthOptions>>().Any()
            || app.Services.GetServices<IPostConfigureOptions<BasicAuthOptions>>().Any();
        return configured
            ? app.Services.GetRequiredService<IOptions<BasicAuthOptions>>().Value
            : app.Configuration.GetSection(BasicAuthOptions.SectionName).Get<BasicAuthOptions>() ?? new();
    }

    // A cookie session wins; otherwise the configured single-owner Basic credential; otherwise,
    // with no credential configured, the open kernel behaves as the auto-provisioned owner.
    private static CallerContext? ResolvePrincipal(HttpContext context, BasicCredential? credential)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var principalId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(principalId))
            {
                var accountId = context.User.FindFirst("intochat.account")?.Value ?? principalId;
                var claimBrain = context.User.FindFirst("intochat.brain")?.Value;
                return Build(principalId, accountId, context, claimBrain);
            }
        }

        if (credential is null)
        {
            return Build(DefaultLogin, DefaultLogin, context);
        }

        if (!credential.Matches(context.Request.Headers.Authorization))
        {
            return null;
        }

        var owner = string.Equals(credential.Username, DefaultLogin, StringComparison.Ordinal);
        return Build(owner ? DefaultLogin : credential.Username, owner ? DefaultLogin : credential.Username, context);
    }

    private static CallerContext Build(string principalId, string accountId, HttpContext context, string? claimBrain = null)
    {
        var brainId = context.Request.RouteValues.TryGetValue("brainId", out var routeBrain)
            && routeBrain is string { Length: > 0 } brainValue
                ? brainValue
                : claimBrain is { Length: > 0 } ? claimBrain : DefaultLogin;
        return new CallerContext
        {
            PrincipalId = principalId,
            AccountId = accountId,
            BrainId = brainId,
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        };
    }

    private static bool IsAnonymous(HttpRequest request)
    {
        // Preflight carries no Authorization header by design; CORS answers it.
        if (HttpMethods.IsOptions(request.Method))
        {
            return true;
        }

        // The login and session endpoints exist to establish the session the gate would require.
        if (request.Path.Value?.ToLowerInvariant() is "/identity/register" or "/identity/login" or "/identity/session" or "/identity/logout")
        {
            return true;
        }

        foreach (var path in AnonymousPaths)
        {
            if (request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal sealed class BasicCredential
    {
        private readonly byte[] _expected;

        private BasicCredential(string username, string password)
            => _expected = Encoding.UTF8.GetBytes($"{username}:{password}");

        public string Username { get; private init; } = "";

        public static BasicCredential? FromOptions(BasicAuthOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var username = options.Username;
            var password = options.Password;

            return string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
                ? null
                : new BasicCredential(username, password) { Username = username };
        }

        public bool Matches(string? authorizationHeader)
        {
            if (string.IsNullOrEmpty(authorizationHeader))
            {
                return false;
            }

            var separator = authorizationHeader.IndexOf(' ');
            if (separator <= 0
                || !authorizationHeader.AsSpan(0, separator).Equals(BasicScheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var encoded = authorizationHeader.AsSpan(separator + 1).Trim();

            // Bounded before the stackalloc: the header length is attacker-controlled.
            if (encoded.Length is 0 or > MaxEncodedCredentialChars)
            {
                return false;
            }

            Span<byte> presented = stackalloc byte[MaxEncodedCredentialChars / 4 * 3];
            if (!Convert.TryFromBase64Chars(encoded, presented, out var written))
            {
                return false;
            }

            // FixedTimeEquals over the whole "user:pass" pair: no length or prefix leak.
            return CryptographicOperations.FixedTimeEquals(presented[..written], _expected);
        }
    }
}

