using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

public static class ModuleUserActionBoundary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async ValueTask<IssuedUserAction> IssueAsync(
        IUserActionCustody custody,
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string moduleId,
        string displayText,
        Uri? signInUrl,
        string? state,
        long parkRevision,
        TimeSpan lifetime,
        NeuronId completer,
        Guid actionEpoch,
        CancellationToken cancellationToken,
        string? authorizationCode = null)
    {
        ArgumentNullException.ThrowIfNull(custody);
        var actionMaterial = ProtectActionMaterial(signInUrl, state, authorizationCode);
        return await custody
            .IssueAsync(
                owner,
                task,
                attempt,
                moduleNeuron,
                moduleId,
                displayText,
                actionMaterial,
                parkRevision,
                lifetime,
                completer,
                actionEpoch,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static ValueTask<IssuedUserAction> IssueFromAuthorizationRequiredAsync(
        IUserActionCustody custody,
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string serverKey,
        string serverDisplayName,
        Uri signInUrl,
        string state,
        long parkRevision,
        TimeSpan lifetime,
        NeuronId completer,
        Guid actionEpoch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverDisplayName);
        ArgumentNullException.ThrowIfNull(signInUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        return IssueAsync(
            custody,
            owner,
            task,
            attempt,
            moduleNeuron,
            moduleId: serverKey,
            displayText: $"{serverDisplayName} requires sign-in",
            signInUrl,
            state,
            parkRevision,
            lifetime,
            completer,
            actionEpoch,
            cancellationToken);
    }

    public static UserActionRequired Create(
        NeuronId task,
        AttemptId attempt,
        NeuronId moduleNeuron,
        string moduleId,
        string displayText,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long parkRevision,
        DateTimeOffset expiresAt,
        NeuronId completer)
        => new(
            task,
            attempt,
            moduleNeuron,
            moduleId,
            displayText,
            actionReference,
            actionEpoch,
            parkRevision,
            expiresAt,
            completer);

    public static byte[] ProtectActionMaterial(Uri? signInUrl, string? state, string? authorizationCode = null)
    {
        var material = new ActionMaterial(
            signInUrl?.AbsoluteUri,
            state,
            authorizationCode);
        return JsonSerializer.SerializeToUtf8Bytes(material, JsonOptions);
    }

    public static string SerializeSafeSurface(UserActionRequired action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return JsonSerializer.Serialize(action, JsonOptions);
    }

    public static bool SurfaceContainsSecretFragments(string surface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        return surface.Contains("signInUrl", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("authorizationCode", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("access_token", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("refresh_token", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("client_secret", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("Bearer ", StringComparison.Ordinal)
            || surface.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || surface.Contains("authorityProof", StringComparison.OrdinalIgnoreCase);
    }

    internal static byte[] SerializeCustodyMaterial(
        Guid actionEpoch,
        NeuronId module,
        string moduleId,
        long parkRevision,
        ReadOnlyMemory<byte> actionMaterial,
        NeuronId completer,
        string displayText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);
        var envelope = new CustodyMaterial(
            actionEpoch,
            module.Type,
            module.Owner.Value,
            module.Name,
            moduleId,
            parkRevision,
            Convert.ToBase64String(actionMaterial.Span),
            completer.Type,
            completer.Owner.Value,
            completer.Name,
            displayText);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    internal static CustodyMaterial DeserializeCustodyMaterial(ReadOnlySpan<byte> plaintext)
    {
        var material = JsonSerializer.Deserialize<CustodyMaterial>(plaintext, JsonOptions)
            ?? throw new InvalidOperationException("invalid-user-action-custody");
        if (material.ActionEpoch == Guid.Empty
            || string.IsNullOrWhiteSpace(material.ModuleType)
            || string.IsNullOrWhiteSpace(material.ModuleOwner)
            || string.IsNullOrWhiteSpace(material.ModuleName)
            || string.IsNullOrWhiteSpace(material.ModuleId)
            || string.IsNullOrWhiteSpace(material.CompleterType)
            || string.IsNullOrWhiteSpace(material.CompleterOwner)
            || string.IsNullOrWhiteSpace(material.CompleterName)
            || string.IsNullOrWhiteSpace(material.DisplayText))
        {
            throw new InvalidOperationException("invalid-user-action-custody");
        }

        return material;
    }

    internal sealed record CustodyMaterial(
        Guid ActionEpoch,
        string ModuleType,
        string ModuleOwner,
        string ModuleName,
        string ModuleId,
        long ParkRevision,
        string ActionMaterialBase64,
        string CompleterType,
        string CompleterOwner,
        string CompleterName,
        string DisplayText);

    private sealed record ActionMaterial(string? SignInUrl, string? State, string? AuthorizationCode);
}
