using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Brain.Contracts;

namespace Brain.Modules.Flutter;

public sealed record FlutterGatewaySession(
    string OwnerId,
    string SpaceId,
    string CallerKey,
    IReadOnlySet<string> GrantedContracts)
{
    public static FlutterGatewaySession FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            throw new BrainException("auth.required", "an authenticated Flutter session is required");

        var ownerId = RequiredClaim(principal, "digitalbrain:owner");
        var spaceId = RequiredClaim(principal, "digitalbrain:space");
        var callerKey = new NeuronAddress(ownerId, spaceId, "session/flutter").ToGrainKey();
        var grants = principal.FindAll("digitalbrain:grant")
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        return new FlutterGatewaySession(ownerId, spaceId, callerKey, grants);
    }

    private static string RequiredClaim(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirst(type)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            throw new BrainException("auth.required", $"authenticated session is missing {type}");
        return value;
    }
}

public sealed class FlutterGatewayPolicy
{
    public const int MaximumInputBytes = 32_768;

    private readonly ConcurrentDictionary<string, byte> _mutationCommands = new(StringComparer.Ordinal);

    public NeuronAddress AuthorizeTarget(FlutterGatewaySession session, string target)
    {
        NeuronAddress address;
        try
        {
            address = NeuronAddress.Parse(target);
        }
        catch (ArgumentException)
        {
            throw new BrainException("input.invalid", "target address is malformed");
        }

        if (!string.Equals(address.OwnerId, session.OwnerId, StringComparison.Ordinal) ||
            !string.Equals(address.SpaceId, session.SpaceId, StringComparison.Ordinal))
            throw new BrainException(BrainErrors.GrantDenied, "target is outside the authenticated owner and space");

        return address;
    }

    public NeuronAddress AuthorizeMutation(
        FlutterGatewaySession session,
        string target,
        string contract,
        string inputJson,
        string commandId)
    {
        var address = AuthorizeTarget(session, target);
        if (string.IsNullOrWhiteSpace(contract) || !session.GrantedContracts.Contains(contract))
            throw new BrainException(BrainErrors.GrantMissing, $"session lacks {contract}");
        if (string.IsNullOrWhiteSpace(inputJson) || Encoding.UTF8.GetByteCount(inputJson) > MaximumInputBytes)
            throw new BrainException("input.invalid", $"input exceeds {MaximumInputBytes} bytes");
        if (string.IsNullOrWhiteSpace(commandId) || commandId.Length > 256)
            throw new BrainException("input.invalid", "commandId is required and bounded");

        var replayKey = $"{session.CallerKey}\n{commandId}";
        if (!_mutationCommands.TryAdd(replayKey, 0))
            throw new BrainException("command.replayed", "mutation commandId was already used");

        return address;
    }
}
