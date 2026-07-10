using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Core.V2;

public sealed record V2AdminPolicy(bool Enabled, bool RequireOperatorAssurance, IReadOnlySet<string> AllowedCapabilities);
public sealed record V2OperatorBootstrapResult(RequestContext Context, string SessionId);

/// <summary>Fresh-install operator bootstrap. The bootstrap secret is random, single-use, and never persisted in clear text.</summary>
public sealed class V2OperatorBootstrap(V2SessionManager sessions, V2AdminPolicy policy)
{
    private int _consumed;
    private string? _configuredHash;

    public void ConfigureBootstrapSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Bootstrap secret is required.", nameof(secret));
        _configuredHash = Hash(secret);
    }

    public bool TryConsume(string secret, TenantId tenantId, WorkspaceId workspaceId, string principalId, out V2OperatorBootstrapResult? result)
    {
        result = null;
        if (!policy.Enabled || (policy.RequireOperatorAssurance && !policy.AllowedCapabilities.Contains("brain.admin"))) return false;
        if (_configuredHash is null || !FixedEquals(_configuredHash, Hash(secret)) || Interlocked.Exchange(ref _consumed, 1) != 0) return false;
        var sessionId = "v2-admin-" + Guid.NewGuid().ToString("N");
        var context = new RequestContext(tenantId, workspaceId, new PrincipalRef(principalId, PrincipalKind.Operator), sessionId, AuthAssurance.OperatorBootstrap, Guid.NewGuid().ToString("N"), null, new HashSet<string>(policy.AllowedCapabilities, StringComparer.Ordinal));
        _ = sessions.Create(context, TimeSpan.FromMinutes(15));
        result = new V2OperatorBootstrapResult(context, sessionId);
        return true;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
}
