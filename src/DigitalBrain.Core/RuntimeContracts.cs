using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orleans;

namespace DigitalBrain.Core.Runtime;

public enum PrincipalKind { User, Service, Operator }
public enum AuthAssurance { None, Password, Oidc, MutualTls, OperatorBootstrap }

[GenerateSerializer, Alias("digitalbrain.v2.tenant-id")]
public readonly record struct TenantId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v2.workspace-id")]
public readonly record struct WorkspaceId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}
[GenerateSerializer, Alias("digitalbrain.v2.principal-ref")]
public readonly record struct PrincipalRef([property: Id(0)] string Value, [property: Id(1)] PrincipalKind Kind);

[GenerateSerializer, Alias("digitalbrain.v2.request-context")]
public sealed record RequestContext(
    [property: Id(0)] TenantId TenantId,
    [property: Id(1)] WorkspaceId WorkspaceId,
    [property: Id(2)] PrincipalRef Principal,
    [property: Id(3)] string SessionId,
    [property: Id(4)] AuthAssurance Assurance,
    [property: Id(5)] string CorrelationId,
    [property: Id(6)] string? IdempotencyKey,
    [property: Id(7)] IReadOnlySet<string> Grants,
    [property: Id(8)] string? ConversationId = null);

public static class SessionAudiences
{
    public const string Mcp = "digitalbrain-v2";
    public const string Ui = "digitalbrain-v2-ui";

    public static string RequireFixedMcp(string? configuredAudience)
    {
        if (configuredAudience is null) return Mcp;
        if (!string.Equals(configuredAudience, Mcp, StringComparison.Ordinal))
            throw new InvalidOperationException("The MCP transport audience is fixed and cannot be empty, aliased, or shared with the UI transport.");
        return Mcp;
    }
}

public static class RequestScope
{
    public static string Id(RequestContext context) => Id(
        context.TenantId,
        context.WorkspaceId,
        context.Principal);

    public static string Id(TenantId tenantId, WorkspaceId workspaceId, PrincipalRef principal)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tenant = tenantId.Value,
            workspace = workspaceId.Value,
            principalKind = (int)principal.Kind,
            principal = principal.Value
        });
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }
}

public static class GrainIds
{
    public static string Aggregate(TenantId tenant, WorkspaceId workspace, string aggregate) =>
        ScopePrefix(tenant, workspace) + "aggregate/" + Segment(aggregate);
    public static string Conversation(TenantId tenant, WorkspaceId workspace, string conversation) =>
        ScopePrefix(tenant, workspace) + "conversation/" + Segment(conversation);
    public static string Workflow(TenantId tenant, WorkspaceId workspace, string workflow) =>
        ScopePrefix(tenant, workspace) + "workflow/" + Segment(workflow);

    public static string ScopePrefix(TenantId tenant, WorkspaceId workspace) =>
        $"v2/{Segment(tenant.Value)}/{Segment(workspace.Value)}/";

    public static bool IsInScope(string? grainId, TenantId tenant, WorkspaceId workspace) =>
        !string.IsNullOrWhiteSpace(grainId) && grainId.StartsWith(ScopePrefix(tenant, workspace), StringComparison.Ordinal);

    private static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty grain id component is required.", nameof(value));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed class SessionTokenService
{
    private const string StructuredPrefix = "v3s";
    private const string ActionCapabilityPrefix = "v1a";
    private const string ActionCapabilityDomain = "digitalbrain.surface-action.v1\0";
    private const string ActionBindingProofDomain = "digitalbrain.surface-action.binding.v1\0";
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;
    public SessionTokenService(byte[] key, TimeProvider? timeProvider = null)
    {
        if (key.Length < 32) throw new ArgumentException("The session signing key must be at least 256 bits.", nameof(key));
        _key = key.ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
    public string Issue(
        RequestContext context,
        TimeSpan lifetime,
        string audience = SessionAudiences.Mcp,
        long sessionVersion = 1)
    {
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (string.IsNullOrWhiteSpace(audience)) throw new ArgumentException("A non-empty session audience is required.", nameof(audience));
        if (sessionVersion < 1) throw new ArgumentOutOfRangeException(nameof(sessionVersion));
        if (string.IsNullOrWhiteSpace(context.SessionId) || string.IsNullOrWhiteSpace(context.TenantId.Value) ||
            string.IsNullOrWhiteSpace(context.WorkspaceId.Value) || string.IsNullOrWhiteSpace(context.Principal.Value))
            throw new ArgumentException("A complete request context is required.", nameof(context));
        if (context.SessionId.Length > 256 || context.TenantId.Value.Length > 256 || context.WorkspaceId.Value.Length > 256 ||
            context.Principal.Value.Length > 256 || audience.Length > 128 || context.Grants.Count > 64 ||
            context.Grants.Any(static grant => string.IsNullOrWhiteSpace(grant) || grant.Length > 128))
            throw new ArgumentException("Session claims exceed the signed transport bound.", nameof(context));

        var now = _timeProvider.GetUtcNow();
        var claims = new SessionClaims(
            3,
            context.SessionId,
            sessionVersion,
            context.TenantId.Value,
            context.WorkspaceId.Value,
            context.Principal.Value,
            context.Principal.Kind,
            context.Assurance,
            audience,
            context.Grants.Order(StringComparer.Ordinal).ToArray(),
            now.ToUnixTimeSeconds(),
            now.Add(lifetime).ToUnixTimeSeconds());
        var encoded = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var body = StructuredPrefix + "." + encoded;
        var signature = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body)));
        return body + "." + signature;
    }

    public bool TryValidate(string token, out RequestContext context)
        => TryValidateCore(token, expectedAudience: null, out context, out _, out _);

    public bool TryValidate(string token, string expectedAudience, out RequestContext context)
    {
        context = default!;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out _, out _);
    }

    public bool TryValidate(string token, string expectedAudience, out RequestContext context, out DateTimeOffset expiresAt)
    {
        context = default!;
        expiresAt = default;
        return !string.IsNullOrWhiteSpace(expectedAudience) && TryValidateCore(token, expectedAudience, out context, out expiresAt, out _);
    }

    public bool TryValidate(
        string token,
        string expectedAudience,
        out RequestContext context,
        out DateTimeOffset expiresAt,
        out long sessionVersion)
    {
        context = default!;
        expiresAt = default;
        sessionVersion = 0;
        return !string.IsNullOrWhiteSpace(expectedAudience) &&
               TryValidateCore(token, expectedAudience, out context, out expiresAt, out sessionVersion);
    }

    public string IssueActionCapability(
        RequestContext context,
        string bindingId,
        string surfaceId,
        int surfaceRevision,
        string bindingTokenHash,
        DateTimeOffset expiresAt)
    {
        if (!IsActionCapabilityInputValid(context, bindingId, surfaceId, surfaceRevision, bindingTokenHash))
            throw new ArgumentException("A bounded action binding and complete request context are required.");

        var now = _timeProvider.GetUtcNow();
        if (expiresAt <= now || expiresAt > now.Add(UiProtocol.ActionTokenLifetime))
            throw new ArgumentOutOfRangeException(nameof(expiresAt));

        var claims = new ActionCapabilityClaims(
            1,
            RequestScope.Id(context),
            context.SessionId,
            bindingId,
            surfaceId,
            surfaceRevision,
            ActionBindingProof(bindingTokenHash),
            now.ToUnixTimeSeconds(),
            expiresAt.ToUnixTimeSeconds());
        var body = ActionCapabilityPrefix + "." + Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes(ActionCapabilityDomain + body)));
        return body + "." + signature;
    }

    public bool TryValidateActionCapability(
        string token,
        RequestContext context,
        string bindingId,
        string surfaceId,
        int surfaceRevision,
        string bindingTokenHash)
    {
        if (!IsActionCapabilityInputValid(context, bindingId, surfaceId, surfaceRevision, bindingTokenHash) ||
            string.IsNullOrWhiteSpace(token) || token.Length > 4_096)
            return false;

        var parts = token.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], ActionCapabilityPrefix, StringComparison.Ordinal)) return false;
        var body = string.Join('.', parts.Take(2));
        var expectedSignature = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ActionCapabilityDomain + body));
        byte[] actualSignature;
        try { actualSignature = Convert.FromHexString(parts[2]); }
        catch (FormatException) { return false; }
        if (actualSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(actualSignature, expectedSignature))
            return false;

        ActionCapabilityClaims? claims;
        try { claims = JsonSerializer.Deserialize<ActionCapabilityClaims>(Base64UrlDecode(parts[1])); }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException) { return false; }
        if (claims is null || claims.Version != 1 ||
            !string.Equals(claims.ScopeId, RequestScope.Id(context), StringComparison.Ordinal) ||
            !string.Equals(claims.SessionId, context.SessionId, StringComparison.Ordinal) ||
            !string.Equals(claims.BindingId, bindingId, StringComparison.Ordinal) ||
            !string.Equals(claims.SurfaceId, surfaceId, StringComparison.Ordinal) ||
            claims.SurfaceRevision != surfaceRevision ||
            !FixedHashEquals(claims.BindingProof, ActionBindingProof(bindingTokenHash)))
            return false;

        DateTimeOffset issuedAt;
        DateTimeOffset expiresAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(claims.IssuedAtUnixSeconds);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException) { return false; }
        var now = _timeProvider.GetUtcNow();
        return expiresAt > now && issuedAt <= now.AddMinutes(5) &&
               expiresAt > issuedAt && expiresAt <= issuedAt.Add(UiProtocol.ActionTokenLifetime);
    }

    private bool TryValidateCore(
        string token,
        string? expectedAudience,
        out RequestContext context,
        out DateTimeOffset expiresAt,
        out long sessionVersion)
    {
        context = default!;
        expiresAt = default;
        sessionVersion = 0;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384) return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != StructuredPrefix) return false;
        var body = string.Join('.', parts.Take(2));
        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(body));
        byte[] actual;
        try { actual = Convert.FromHexString(parts[2]); } catch (FormatException) { return false; }
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) return false;

        SessionClaims? claims;
        try { claims = JsonSerializer.Deserialize<SessionClaims>(Base64UrlDecode(parts[1])); }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { return false; }
        if (claims is null || claims.Version != 3 || claims.SessionVersion < 1 || string.IsNullOrWhiteSpace(claims.SessionId) ||
            string.IsNullOrWhiteSpace(claims.TenantId) || string.IsNullOrWhiteSpace(claims.WorkspaceId) ||
            string.IsNullOrWhiteSpace(claims.PrincipalId) || string.IsNullOrWhiteSpace(claims.Audience) ||
            claims.SessionId.Length > 256 || claims.TenantId.Length > 256 || claims.WorkspaceId.Length > 256 ||
            claims.PrincipalId.Length > 256 || claims.Audience.Length > 128 || claims.Grants is null || claims.Grants.Length > 64 ||
            claims.Grants.Any(static grant => string.IsNullOrWhiteSpace(grant) || grant.Length > 128) ||
            !Enum.IsDefined(claims.PrincipalKind) || !Enum.IsDefined(claims.Assurance) ||
            (expectedAudience is not null && !string.Equals(claims.Audience, expectedAudience, StringComparison.Ordinal))) return false;
        DateTimeOffset issuedAt;
        DateTimeOffset expiry;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(claims.IssuedAtUnixSeconds);
            expiry = DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException) { return false; }
        var now = _timeProvider.GetUtcNow();
        if (expiry <= now || issuedAt > now.AddMinutes(5) || expiry <= issuedAt) return false;
        var grants = (claims.Grants ?? [])
            .Where(static grant => !string.IsNullOrWhiteSpace(grant))
            .ToHashSet(StringComparer.Ordinal);
        context = new RequestContext(
            new(claims.TenantId),
            new(claims.WorkspaceId),
            new(claims.PrincipalId, claims.PrincipalKind),
            claims.SessionId,
            claims.Assurance,
            Guid.NewGuid().ToString("N"),
            null,
            grants);
        expiresAt = expiry;
        sessionVersion = claims.SessionVersion;
        return true;
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsActionCapabilityInputValid(
        RequestContext context,
        string? bindingId,
        string? surfaceId,
        int surfaceRevision,
        string? bindingTokenHash) =>
        !string.IsNullOrWhiteSpace(context.SessionId) && context.SessionId.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.TenantId.Value) && context.TenantId.Value.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.WorkspaceId.Value) && context.WorkspaceId.Value.Length <= 256 &&
        !string.IsNullOrWhiteSpace(context.Principal.Value) && context.Principal.Value.Length <= 256 &&
        !string.IsNullOrWhiteSpace(bindingId) && bindingId.Length <= 256 &&
        !string.IsNullOrWhiteSpace(surfaceId) && surfaceId.Length <= 256 &&
        surfaceRevision > 0 && IsSha256Hash(bindingTokenHash);

    private static bool IsSha256Hash(string? value)
    {
        if (value is not { Length: 64 }) return false;
        try { return Convert.FromHexString(value).Length == 32; }
        catch (FormatException) { return false; }
    }

    private static bool FixedHashEquals(string? first, string? second)
    {
        if (first is not { } firstHash || second is not { } secondHash ||
            !IsSha256Hash(firstHash) || !IsSha256Hash(secondHash))
            return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(firstHash), Convert.FromHexString(secondHash));
    }

    private string ActionBindingProof(string bindingTokenHash) =>
        Convert.ToHexString(HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes(ActionBindingProofDomain + bindingTokenHash.ToLowerInvariant())));

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => throw new FormatException() };
        return Convert.FromBase64String(padded);
    }

    private sealed record SessionClaims(
        int Version,
        string SessionId,
        long SessionVersion,
        string TenantId,
        string WorkspaceId,
        string PrincipalId,
        PrincipalKind PrincipalKind,
        AuthAssurance Assurance,
        string Audience,
        string[] Grants,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds);

    private sealed record ActionCapabilityClaims(
        int Version,
        string ScopeId,
        string SessionId,
        string BindingId,
        string SurfaceId,
        int SurfaceRevision,
        string BindingProof,
        long IssuedAtUnixSeconds,
        long ExpiresAtUnixSeconds);
}

public sealed record SessionPair(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    DateTimeOffset AccessExpiresAt = default,
    string Audience = SessionAudiences.Mcp);

public enum Sensitivity { Public, Internal, Confidential, Secret }
public static class Redaction
{
    public static string SafeSummary(string? value, Sensitivity classification = Sensitivity.Internal) =>
        classification == Sensitivity.Secret ? "[REDACTED]" : value is null ? string.Empty : value.Length > 256 ? value[..256] + "…" : value;
    public static JsonElement Redact(JsonElement value, Sensitivity classification) =>
        classification == Sensitivity.Secret ? JsonElement.Parse("\"[REDACTED]\"") : value.Clone();
}

[GenerateSerializer, Alias("digitalbrain.v2.command-envelope")]
public sealed record CommandEnvelope([property: Id(0)] string Type, [property: Id(1)] int Version, [property: Id(2)] string CommandId, [property: Id(3)] RequestContext Context, [property: Id(4)] JsonElement Payload);
