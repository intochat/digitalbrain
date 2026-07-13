using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Kernel.Runtime;

public sealed class InoEffectPlanAuthority(IRuntimeStateKeyRing keys)
{
    private const string Prefix = "plan-v1.";
    private const string ExecutionPurpose = "digitalbrain-ino-effect-execution-v1";

    public string Issue(string planId, string actorScope, string toolId, string safeSummary)
    {
        DemandScopeHash(planId, nameof(planId));
        DemandScopeHash(actorScope, nameof(actorScope));
        DemandToolId(toolId);
        DemandSafeSummary(safeSummary);
        var summaryDigest = SummaryDigest(safeSummary);
        var signature = Sign(planId, actorScope, toolId, summaryDigest);
        return Prefix + planId + "." + summaryDigest + "." + Base64UrlEncode(signature);
    }

    public bool TryValidate(
        string? scope,
        string actorScope,
        string toolId,
        string safeSummary,
        out string planId)
    {
        if (!TryValidateToken(scope, actorScope, toolId, out planId, out var summaryDigest) ||
            !IsSafeSummary(safeSummary))
            return false;
        var expectedDigest = SummaryDigest(safeSummary);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedDigest),
            Encoding.ASCII.GetBytes(summaryDigest));
    }

    public bool TryValidateToken(
        string? scope,
        string actorScope,
        string toolId,
        out string planId,
        out string summaryDigest)
    {
        planId = string.Empty;
        summaryDigest = string.Empty;
        if (!IsScopeHash(actorScope) || !IsToolId(toolId) || scope is null ||
            !scope.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var planSeparator = scope.IndexOf('.', Prefix.Length);
        if (planSeparator < 0 || planSeparator != Prefix.Length + 64 ||
            !IsScopeHash(scope.AsSpan(Prefix.Length, 64)))
            return false;
        var digestSeparator = scope.IndexOf('.', planSeparator + 1);
        if (digestSeparator != planSeparator + 65 ||
            !IsScopeHash(scope.AsSpan(planSeparator + 1, 64)))
            return false;

        var candidatePlanId = scope.Substring(Prefix.Length, 64);
        var candidateSummaryDigest = scope.Substring(planSeparator + 1, 64);
        if (!TryBase64UrlDecode(scope[(digestSeparator + 1)..], out var supplied) || supplied.Length != 32)
            return false;
        var expected = Sign(candidatePlanId, actorScope, toolId, candidateSummaryDigest);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            return false;

        planId = candidatePlanId;
        summaryDigest = candidateSummaryDigest;
        return true;
    }

    public static bool MatchesSummary(string safeSummary, string summaryDigest)
    {
        if (!IsSafeSummary(safeSummary) || !IsScopeHash(summaryDigest)) return false;
        var expected = SummaryDigest(safeSummary);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(summaryDigest));
    }

    public string IssueExecutionProof(
        string planId,
        string actorScope,
        string operationId,
        string toolId,
        string effectId,
        string providerIdempotencyKey)
    {
        DemandScopeHash(planId, nameof(planId));
        DemandScopeHash(actorScope, nameof(actorScope));
        DemandToolId(toolId);
        DemandBoundedId(operationId, nameof(operationId));
        DemandBoundedId(effectId, nameof(effectId));
        DemandBoundedId(providerIdempotencyKey, nameof(providerIdempotencyKey));
        return Base64UrlEncode(SignExecution(
            planId, actorScope, operationId, toolId, effectId, providerIdempotencyKey));
    }

    public bool ValidateExecutionProof(
        string? proof,
        string planId,
        string actorScope,
        string operationId,
        string toolId,
        string effectId,
        string providerIdempotencyKey)
    {
        if (!IsScopeHash(planId) || !IsScopeHash(actorScope) || !IsToolId(toolId) ||
            !IsBoundedId(operationId) || !IsBoundedId(effectId) || !IsBoundedId(providerIdempotencyKey) ||
            proof is null || !TryBase64UrlDecode(proof, out var supplied) || supplied.Length != 32)
            return false;
        var expected = SignExecution(
            planId, actorScope, operationId, toolId, effectId, providerIdempotencyKey);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private byte[] Sign(string planId, string actorScope, string toolId, string summaryDigest)
    {
        using var hmac = new HMACSHA256(keys.SigningKey.ToArray());
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(
            "digitalbrain-ino-effect-plan-v1\0" + planId + "\0" + actorScope + "\0" + toolId + "\0" + summaryDigest));
    }

    private byte[] SignExecution(
        string planId,
        string actorScope,
        string operationId,
        string toolId,
        string effectId,
        string providerIdempotencyKey)
    {
        using var hmac = new HMACSHA256(keys.SigningKey.ToArray());
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(
            ExecutionPurpose + "\0" + planId + "\0" + actorScope + "\0" + operationId + "\0" + toolId +
            "\0" + effectId + "\0" + providerIdempotencyKey));
    }

    private static void DemandScopeHash(string value, string name)
    {
        if (!IsScopeHash(value))
            throw new ArgumentException("A lowercase SHA-256 scope hash is required.", name);
    }

    private static bool IsScopeHash(string value) => IsScopeHash(value.AsSpan());

    private static bool IsScopeHash(ReadOnlySpan<char> value)
    {
        if (value.Length != 64) return false;
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    private static void DemandToolId(string value)
    {
        if (!IsToolId(value))
            throw new ArgumentException("A bounded typed tool identifier is required.", nameof(value));
    }

    private static void DemandBoundedId(string value, string name)
    {
        if (!IsBoundedId(value)) throw new ArgumentException("A bounded execution identifier is required.", name);
    }

    private static bool IsBoundedId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);

    private static bool IsToolId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);

    private static void DemandSafeSummary(string value)
    {
        if (!IsSafeSummary(value))
            throw new ArgumentException("A bounded safe approval summary is required.", nameof(value));
    }

    private static bool IsSafeSummary(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= InoEffectPlanTransitions.MaximumSafeTextLength &&
        !value.Any(char.IsControl);

    private static string SummaryDigest(string safeSummary) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(safeSummary)));

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length != 43 || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return false;
        var padded = value.Replace('-', '+').Replace('_', '/') + "=";
        try
        {
            bytes = Convert.FromBase64String(padded);
            return string.Equals(Base64UrlEncode(bytes), value, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
