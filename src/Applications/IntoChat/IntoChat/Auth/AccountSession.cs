using System.Security.Cryptography;
using System.Text;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

namespace IntoChat;

// The authenticated edge. A cookie session (issued by the identity module) or, for the
// single-owner bootstrap, the configured Basic credential decides the principal; when neither is
// configured the kernel stays open, which is the local and test posture. Resolving a principal
// stamps a CallerContext on the Orleans RequestContext so it travels with grain calls. Replaces
// the former Basic-only gate.
internal static class AccountSession
{
    public const string DefaultLogin = "owner";
    public const string CheckPath = "/auth/check";
    public const string CapabilitiesPath = "/session/capabilities";

    private const string BasicScheme = "Basic";

    // Generous for "user:pass" while keeping the decode buffer stack-safe.
    private const int MaxEncodedCredentialChars = 1024;

    // Probed by the shell's login screen and by container probes; never gated.
    private static readonly string[] AnonymousPaths = ["/health", "/alive", "/ui/inbox"];

    public static WebApplication UseAccountSession(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var credential = BasicCredential.FromOptions(IntoChatConfiguration.ResolveAuthOptions(app));

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
            var workspace = context.Request.RouteValues["workspaceId"] as string
                ?? context.Request.RouteValues["workspace"] as string;
            if (!string.IsNullOrWhiteSpace(workspace) &&
                context.User.Identity?.IsAuthenticated == true)
            {
                var access = context.RequestServices.GetRequiredService<IWorkspaceAccess>();
                if (!await access.CanAccessAsync(principal.PrincipalId, workspace, context.RequestAborted))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });

        // Inside the gate, so reaching it at all proves the session is good.
        app.MapGet(CheckPath, static () => Results.NoContent());

        // Read-only server capabilities the shell gates developer-only surfaces on. Developer mode
        // is the same server setting the agent tool policy reads, so a client cannot grant itself
        // the Behaviors console by editing local preferences.
        app.MapGet(CapabilitiesPath, static (IConfiguration configuration) => Results.Ok(
            new SessionCapabilities(AgentToolPolicy.DeveloperModeEnabled(configuration["IntoChat:DeveloperMode"]))));

        return app;
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
                var claimWorkspace = context.User.FindFirst("intochat.workspace")?.Value;
                return Build(principalId, accountId, context, claimWorkspace);
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

    private static CallerContext Build(string principalId, string accountId, HttpContext context, string? claimWorkspace = null)
    {
        var workspaceId = context.Request.RouteValues.TryGetValue("workspaceId", out var routeWorkspace)
            && routeWorkspace is string { Length: > 0 } value
                ? value
                : claimWorkspace is { Length: > 0 } ? claimWorkspace : DefaultLogin;
        return new CallerContext
        {
            PrincipalId = principalId,
            AccountId = accountId,
            WorkspaceId = workspaceId,
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

        public static BasicCredential? FromConfiguration(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return FromOptions(configuration.GetSection(BasicAuthOptions.SectionName).Get<BasicAuthOptions>() ?? new());
        }

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

internal sealed record SessionCapabilities(bool DeveloperMode);
