using System.Security.Cryptography;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Ui;

namespace DigitalBrain.Kernel.Auth;

using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Runtime;


[GrainType("digitalbrain.user-session.v1")]
public sealed class UserSessionNeuron(ILogger<UserSessionNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IUserSessionNeuron
{
    private const int PasswordHashIterations = 100_000;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private const string DefaultWorkspaceId = WorkspaceIds.Default;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        if (!ActiveSessions().Any())
        {
            await BroadcastAsync(LoginSurface(), ct);
        }
    }

    public async Task HandleAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var username = NormalizeUsername(request.Username);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "flutter" : request.ClientId.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
        {
            await RejectAsync(username, "username and password are required", clientId, cancellationToken);
            return;
        }

        if (!IsValidUsernameCharset(username))
        {
            await RejectAsync(username, "username may not contain invalid characters ('/', whitespace, or quotes)", clientId, cancellationToken);
            return;
        }

        // Dev-only convenience: the seeded credentials always authenticate (provisioned on first use,
        // password-bypassed if an account already exists) so a fresh checkout can sign in without setup.
        // Off outside Development, so the deployed app is unaffected.
        var isDevCredentials = DevAuthEnabled() && DevAuth.Matches(username, request.Password);

        var users = RegisteredUsers().ToList();
        var user = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            if (!isDevCredentials && (!AllowFirstUserProvisioning() || users.Count > 0))
            {
                await RejectAsync(username, "invalid username or password", clientId, cancellationToken);
                return;
            }

            user = CreateLocalUser(username, request.Password);
            await FireAsync(user, cancellationToken);
        }
        else if (!isDevCredentials && !VerifyPassword(request.Password, user.PasswordSaltBase64, user.PasswordHashBase64))
        {
            await RejectAsync(username, "invalid username or password", clientId, cancellationToken);
            return;
        }

        var sessionId = "session-" + Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime);

        await FireAsync(new LoginSucceeded(user.UserId, sessionId, user.DisplayName, user.Roles, clientId), cancellationToken);
        await FireAsync(new UserSessionCreated(user.UserId, sessionId, expiresAt, clientId), cancellationToken);

        await BroadcastSignedInAsync(user, sessionId, clientId, cancellationToken);

        // Reuse the existing product-surface startup path after a real session exists.
        await GrainFactory.GetGrain<IAspireNeuron>("aspire-main").FireAsync(new StartDistributedApp("digitalbrain"), cancellationToken);
        await BroadcastProductHomeAsync(user, sessionId, clientId, cancellationToken);
    }

    public async Task HandleAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "flutter" : request.ClientId.Trim();

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            await FireAsync(new UserSessionEnded(request.SessionId, clientId), cancellationToken);
        }

        await BroadcastAsync(LoginSurface(clientId: clientId), cancellationToken);
    }

    public Task<UserSessionState?> GetSessionAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        return Task.FromResult(ResolveSession(sessionId, SessionJournal()));
    }

    public Task<UserSessionState?> GetSessionByClientIdAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        return Task.FromResult(ResolveSessionByClientId(clientId, SessionJournal()));
    }

    public Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null) =>
        Task.FromResult(LoginSurface(clientId: clientId));

    // In Development the login surface is pre-filled with seeded dev credentials that always authenticate
    // (see HandleAsync). Disabled outside Development unless DigitalBrain:Auth:DevAutoLogin is set explicitly.
    private UiSurface LoginSurface(string? error = null, string? clientId = null)
    {
        var resolvedClientId = string.IsNullOrWhiteSpace(clientId) ? "flutter" : clientId;
        return DevAuthEnabled()
            ? UiSurfaceSamples.Login(error, resolvedClientId, DevAuth.Username, DevAuth.Password)
            : UiSurfaceSamples.Login(error, resolvedClientId);
    }

    private bool DevAuthEnabled() =>
        DevAuth.Enabled(ServiceProvider.GetService<IConfiguration>(), ServiceProvider.GetService<IHostEnvironment>());

    private async Task BroadcastProductHomeAsync(LocalUserRegistered user, string sessionId, string clientId, CancellationToken cancellationToken)
    {
        var userId = user.UserId.Value;
        var taskEvents = OutgoingJournal.Concat(IncomingJournal).ToList();
        var surfaces = new[]
        {
            BuildSignedInShellSurface(user, sessionId, clientId),
            UiSurfaceLiveData.WorkspaceBoundary(userId, DefaultWorkspaceId, clientId),
            UiSurfaceLiveData.TaskManagerFromTasks(taskEvents, userId: userId, clientId: clientId)
        };

        foreach (var surface in surfaces)
        {
            await FireAsync(surface, cancellationToken);
            await BroadcastAsync(surface, cancellationToken);
        }
    }

    private UiSurface BuildSignedInShellSurface(LocalUserRegistered user, string sessionId, string clientId)
    {
        var menuItems = new[]
        {
            MenuItem("INO Chat", "chat"),
            MenuItem("Workspace", UiSurfaceKinds.Workspace),
            MenuItem("Tasks", UiSurfaceKinds.TaskManager),
            new UiWidgetTree(NeuronUiKit.Divider, new Dictionary<string, object?>()),
            new UiWidgetTree(NeuronUiKit.MenuItem, new Dictionary<string, object?>
            {
                ["label"] = "Sign Out",
                ["action"] = UiSurfaceSamples.SynapseAction(
                    "logout",
                    "Sign Out",
                    nameof(LogoutRequest),
                    new Dictionary<string, object?>
                    {
                        ["clientId"] = clientId
                    })
            })
        };

        var tree = new UiWidgetTree(
            NeuronUiKit.Scaffold,
            new Dictionary<string, object?>
            {
                ["title"] = "DigitalBrain",
                ["activeContent"] = "chat",
                ["userId"] = user.UserId.Value,
                ["clientId"] = clientId,
                ["workspaceId"] = DefaultWorkspaceId
            },
            [
                NeuronUiKit.BuildHeader("DigitalBrain", user.DisplayName),
                new(NeuronUiKit.Sidebar, new Dictionary<string, object?> { ["title"] = user.DisplayName }, menuItems),
                new("content", new Dictionary<string, object?>
                {
                    ["defaultView"] = "chat"
                })
            ]);

        return new UiSurface(UiSurface.WidgetTreeKind, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.SurfaceId] = "surface.shell." + user.UserId.Value,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "DigitalBrain",
            [UiSurfaceKeys.Priority] = 100,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["clientId"] = clientId,
            ["workspaceId"] = DefaultWorkspaceId
        });
    }

    private static UiWidgetTree MenuItem(string label, string targetSurfaceKind) =>
        new(NeuronUiKit.MenuItem, new Dictionary<string, object?>
        {
            ["label"] = label,
            ["targetSurfaceKind"] = targetSurfaceKind
        });

    private async Task RejectAsync(string username, string reason, string clientId, CancellationToken cancellationToken)
    {
        await FireAsync(new LoginFailed(username, reason, clientId), cancellationToken);
        await BroadcastAsync(LoginSurface(reason, clientId), cancellationToken);
    }

    private async Task BroadcastSignedInAsync(LocalUserRegistered user, string sessionId, string clientId, CancellationToken cancellationToken)
    {
        var surface = new UiSurface("session-status", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.session." + clientId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Signed In",
            [UiSurfaceKeys.Priority] = 90,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Compact,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["clientId"] = clientId,
            ["status"] = "signed-in",
            ["body"] = $"Signed in as {user.DisplayName}"
        });

        await BroadcastAsync(surface, cancellationToken);
    }

    private async Task BroadcastAsync(UiSurface surface, CancellationToken cancellationToken = default)
    {
        var bus = ServiceProvider.GetService<HomeFeedBus>();
        if (bus is not null)
        {
            await bus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, Self.Value), cancellationToken);
        }
    }

    private IReadOnlyList<Synapse> SessionJournal() =>
        OutgoingJournal
            .Concat(IncomingJournal)
            .DistinctBy(s => s.SynapseId)
            .ToList();

    private UserSessionState? ResolveSession(string sessionId, IReadOnlyList<Synapse> journal)
    {
        var ended = EndedSessionIds(journal);
        if (ended.Contains(sessionId))
        {
            return null;
        }

        var created = journal
            .OfType<UserSessionCreated>()
            .LastOrDefault(s => string.Equals(s.SessionId, sessionId, StringComparison.Ordinal));

        return created is null || !IsActive(created, ended, DateTimeOffset.UtcNow)
            ? null
            : BuildSessionState(created, journal);
    }

    private UserSessionState? ResolveSessionByClientId(string clientId, IReadOnlyList<Synapse> journal)
    {
        var ended = EndedSessionIds(journal);
        var now = DateTimeOffset.UtcNow;
        var created = journal
            .OfType<UserSessionCreated>()
            .Where(s => string.Equals(s.ClientId, clientId, StringComparison.Ordinal))
            .Where(s => IsActive(s, ended, now))
            .OrderBy(s => s.ExpiresAt)
            .LastOrDefault();

        return created is null ? null : BuildSessionState(created, journal);
    }

    private static UserSessionState BuildSessionState(UserSessionCreated created, IReadOnlyList<Synapse> journal)
    {
        var login = journal
            .OfType<LoginSucceeded>()
            .LastOrDefault(s => string.Equals(s.SessionId, created.SessionId, StringComparison.Ordinal));

        return new UserSessionState(
            created.UserId,
            created.SessionId,
            login?.DisplayName ?? created.UserId.Value,
            login?.Roles ?? Array.Empty<string>(),
            created.ExpiresAt,
            Active: true);
    }

    private IEnumerable<UserSessionCreated> ActiveSessions()
    {
        var journal = SessionJournal();
        var ended = EndedSessionIds(journal);
        var now = DateTimeOffset.UtcNow;

        return journal
            .OfType<UserSessionCreated>()
            .Where(s => IsActive(s, ended, now));
    }

    private IEnumerable<LocalUserRegistered> RegisteredUsers() =>
        SessionJournal()
            .OfType<LocalUserRegistered>()
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last());

    private static HashSet<string> EndedSessionIds(IReadOnlyList<Synapse> journal) =>
        journal
            .OfType<UserSessionEnded>()
            .Select(e => e.SessionId)
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsActive(UserSessionCreated session, IReadOnlySet<string> ended, DateTimeOffset now) =>
        session.ExpiresAt > now && !ended.Contains(session.SessionId);

    private bool AllowFirstUserProvisioning()
    {
        var config = ServiceProvider.GetService<IConfiguration>();
        return config?.GetValue("DigitalBrain:Auth:AllowFirstUserProvisioning", true) ?? true;
    }

    private static LocalUserRegistered CreateLocalUser(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt);
        var roles = new[] { "admin", "user" };
        var display = string.Join(" ", username
            .Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        if (string.IsNullOrWhiteSpace(display))
        {
            display = username;
        }

        return new LocalUserRegistered(
            new UserId(username),
            username,
            display,
            Convert.ToBase64String(hash),
            Convert.ToBase64String(salt),
            roles);
    }

    private static bool VerifyPassword(string password, string saltBase64, string expectedHashBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expected = Convert.FromBase64String(expectedHashBase64);
            var actual = HashPassword(password, salt);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordHashIterations,
            HashAlgorithmName.SHA256,
            32);

    private static string NormalizeUsername(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidUsernameCharset(string username) =>
        !username.Any(ch => ch is '/' or '\'' or '"' || char.IsWhiteSpace(ch));
}
