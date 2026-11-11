using DigitalBrain.Brains;

namespace DigitalBrain.Capabilities;

/// <summary>
/// The caller a host stamped on the current call, as a capability port receives it (D-15). Capability contracts take no
/// caller; the runtime reads the stamped caller and passes it explicitly to the port that implements the capability.
/// </summary>
/// <param name="PrincipalId">The calling principal.</param>
/// <param name="AccountId">The account paying for the call.</param>
/// <param name="BrainId">The brain (workspace) the call runs in.</param>
/// <param name="AppId">The app making the call, or null for a user.</param>
/// <param name="ConversationId">The conversation an app call runs for, if any.</param>
public sealed record CapabilityCaller(string PrincipalId, string AccountId, string BrainId, string? AppId = null, string? ConversationId = null)
{
    /// <summary>The logical kind of caller represented by this stamp.</summary>
    public CallerKind Kind => AppId switch
    {
        null or "" => CallerKind.User,
        CapabilityCallers.PlatformApp => CallerKind.Platform,
        CapabilityCallers.SchedulerApp => CallerKind.Scheduler,
        CapabilityCallers.AssistantApp => CallerKind.Agent,
        _ => CallerKind.App,
    };

    /// <summary>The key of the calling app, or null when the caller is a user, the platform or the scheduler.</summary>
    public string? AppKey => Kind == CallerKind.App ? AppId : null;

    /// <summary>The trusted runtime edge that stamped this caller.</summary>
    public TrustedEdge StampedBy => Kind switch
    {
        CallerKind.App => TrustedEdge.AppProxy,
        CallerKind.Scheduler => TrustedEdge.Scheduler,
        CallerKind.Platform => TrustedEdge.Platform,
        _ => TrustedEdge.AuthenticatedHttp,
    };

    /// <summary>The person behind the call. An app keeps the person's principal and names itself with <see cref="AppId"/>.</summary>
    public string PersonId => PrincipalId;

    /// <summary>Optional delegated principal when acting on behalf of another principal.</summary>
    public string? OnBehalfOf { get; init; }

    /// <summary>Whether this caller represents an installed or sandboxed app rather than a person, agent, scheduler, or platform.</summary>
    internal bool IsAppCaller => Kind == CallerKind.App || StampedBy == TrustedEdge.AppProxy || AppPrincipal.IsApp(PrincipalId);

    /// <summary>Whether this caller is admitted to touch <paramref name="brainId"/> (and optionally <paramref name="appId"/>).</summary>
    internal bool CanTouchBrain(string brainId, string? appId = null)
    {
        var sameBrain = string.Equals(BrainId, brainId, StringComparison.Ordinal);
        return Kind switch
        {
            CallerKind.Platform => true,
            CallerKind.User or CallerKind.Agent => sameBrain,
            CallerKind.App => appId is not null && sameBrain && string.Equals(AppId, appId, StringComparison.Ordinal),
            _ => false,
        };
    }

    /// <summary>Whether this caller is a non-app member of <paramref name="brainId"/>.</summary>
    internal bool IsNonAppMemberOf(string brainId)
        => !IsAppCaller && BrainScope.Create(AccountId, BrainId).Id == brainId;

    /// <summary>The stamp shape of an app: the app's own principal and key, with no account or brain until the runtime stamps them.</summary>
    public static CapabilityCaller App(string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        if (appKey is CapabilityCallers.PlatformApp or CapabilityCallers.SchedulerApp or CapabilityCallers.AssistantApp)
        {
            throw new ArgumentException($"'{appKey}' is a reserved caller id, not an app key.", nameof(appKey));
        }
        return new CapabilityCaller(AppPrincipal.Id(appKey), string.Empty, string.Empty, appKey);
    }
}

/// <summary>The logical kind of caller represented by a <see cref="CapabilityCaller"/>.</summary>
public enum CallerKind
{
    /// <summary>A human user calling directly from an authenticated edge.</summary>
    User = 0,
    /// <summary>The brain's own agent runtime, acting with the user's authority.</summary>
    Agent = 1,
    /// <summary>An installed or sandboxed app, named by its key.</summary>
    App = 2,
    /// <summary>The runtime scheduler.</summary>
    Scheduler = 3,
    /// <summary>The platform authority or integration release path.</summary>
    Platform = 4,
    /// <summary>The former name of <see cref="Agent"/>.</summary>
    [Obsolete("The agent kind; removed in 0.13.")]
    Assistant = Agent,
}

/// <summary>The trusted edge that stamped a <see cref="CapabilityCaller"/>.</summary>
public enum TrustedEdge
{
    /// <summary>Authenticated HTTP / RPC ingress edge.</summary>
    AuthenticatedHttp = 0,
    /// <summary>Sandboxed app proxy edge.</summary>
    AppProxy = 1,
    /// <summary>Background scheduler edge.</summary>
    Scheduler = 2,
    /// <summary>Internal platform authority edge.</summary>
    Platform = 3,
}

/// <summary>Canonical factory for constructing stamped <see cref="CapabilityCaller"/> instances.</summary>
public static class CapabilityCallers
{
    /// <summary>Reserved app identifier for platform-stamped calls.</summary>
    public const string PlatformApp = "platform";
    /// <summary>Reserved app identifier for scheduler-stamped calls.</summary>
    public const string SchedulerApp = "scheduler";
    /// <summary>The app identifier the former assistant caller carried; still read as an agent.</summary>
    public const string AssistantApp = "assistant";

    /// <summary>Creates a <see cref="CapabilityCaller"/> for the specified principal, account, brain, and caller kind.</summary>
    public static CapabilityCaller Of(
        string principalId,
        string accountId,
        string brainId,
        CallerKind kind = CallerKind.User,
        string? appId = null,
        string? conversationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        var app = kind switch
        {
            CallerKind.Platform => PlatformApp,
            CallerKind.Scheduler => SchedulerApp,
            CallerKind.Agent => AssistantApp,
            CallerKind.App => string.IsNullOrEmpty(appId) ? "app" : appId,
            _ => null,
        };
        return new CapabilityCaller(principalId, accountId, brainId, app, conversationId);
    }

    /// <summary>Creates a platform-stamped <see cref="CapabilityCaller"/>.</summary>
    public static CapabilityCaller Platform(string principalId, string accountId, string brainId, string? conversationId = null)
        => Of(principalId, accountId, brainId, CallerKind.Platform, conversationId: conversationId);
}

/// <summary>Deterministic principal formatting for installed apps (<c>app:{brain}/apps/{name}</c>).</summary>
public static class AppPrincipal
{
    /// <summary>Prefix for app principal identifiers.</summary>
    public const string Prefix = "app:";

    /// <summary>Formats the deterministic principal identifier for an app key.</summary>
    public static string Id(string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        return Prefix + appKey;
    }

    /// <summary>Returns true when <paramref name="principalId"/> is an app principal.</summary>
    public static bool IsApp(string? principalId) => principalId is { Length: > 4 } && principalId.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Extracts the app key from an app principal identifier, or null when not an app principal.</summary>
    public static string? AppKeyOf(string? principalId) => IsApp(principalId) ? principalId![Prefix.Length..] : null;
}

