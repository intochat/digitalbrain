using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Behaviors;

public sealed class BehaviorKind(IGrainFactory grainFactory) : INeuronKind
{
    private const int MaxSourceBytes = 131072;
    private const int MaxGrants = 32;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => "behavior";
    public string[] Contracts => ["behavior.propose.v1", "behavior.approve.v1", "behavior.decline.v1", "behavior.rollback.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "behavior.propose.v1" => HandleProposeAsync(invocation.InputJson),
            "behavior.approve.v1" => HandleApproveAsync(context, invocation.InputJson),
            "behavior.decline.v1" => HandleDeclineAsync(context, invocation.InputJson),
            "behavior.rollback.v1" => HandleRollbackAsync(context, invocation),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection)
    {
        var folded = Fold(context.Journal);
        var identity = folded.State == "enabled" && folded.ActiveHash is { } activeHash
            ? IdentityKey(context.Address.OwnerId, activeHash)
            : null;

        return JsonSerializer.Serialize(new
        {
            state = folded.State,
            activeHash = folded.ActiveHash,
            identity,
            grants = folded.Grants,
            history = folded.History.Select(entry => new { hash = entry.Hash, state = entry.State })
        }, JsonOptions);
    }

    private static ValueTask<KindResult> HandleProposeAsync(string inputJson)
    {
        var root = ParseJson(inputJson);

        var source = RequireString(root, "source");
        if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
        {
            throw new BrainException("input.invalid", $"source exceeds maximum size of {MaxSourceBytes} bytes");
        }

        var sourceHash = RequireString(root, "sourceHash");
        if (!string.Equals(sourceHash, Sha256Hex(source), StringComparison.Ordinal))
        {
            throw new BrainException("input.invalid", "sourceHash does not match sha256 of source");
        }

        if (!root.TryGetProperty("bddPassed", out var bddPassedElement) ||
            bddPassedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BrainException("input.invalid", "bddPassed field is required");
        }

        if (bddPassedElement.ValueKind != JsonValueKind.True)
        {
            throw new BrainException("input.invalid", "bdd gate failed");
        }

        var grants = ParseGrants(root);
        var grantsHash = ComputeGrantsHash(grants);
        var grantsJson = JsonSerializer.Serialize(grants, JsonOptions);
        var eventPayload = JsonSerializer.Serialize(new { sourceHash, grantsJson, grantsHash, bddPassed = true }, JsonOptions);
        var output = JsonSerializer.Serialize(new { status = "proposed", sourceHash, grantsHash }, JsonOptions);

        return ValueTask.FromResult(new KindResult(output, [("behavior.proposed", eventPayload)]));
    }

    private async ValueTask<KindResult> HandleApproveAsync(NeuronContext context, string inputJson)
    {
        var root = ParseJson(inputJson);
        var sourceHash = RequireString(root, "sourceHash");
        var grantsHash = RequireString(root, "grantsHash");

        var folded = Fold(context.Journal);
        if (folded.State != "proposed" || folded.ActiveHash != sourceHash)
        {
            throw new BrainException("input.invalid", "no matching proposal for sourceHash");
        }

        if (!string.Equals(grantsHash, ComputeGrantsHash(folded.Grants), StringComparison.Ordinal))
        {
            throw new BrainException("input.invalid", "grants changed since review");
        }

        var identity = await IssueGrantsAsync(context, sourceHash, folded.Grants, "grant");

        var grantsJson = JsonSerializer.Serialize(folded.Grants, JsonOptions);
        var eventPayload = JsonSerializer.Serialize(new { sourceHash, grantsJson }, JsonOptions);
        var output = JsonSerializer.Serialize(new { status = "enabled", identity }, JsonOptions);

        return new KindResult(output, [("behavior.enabled", eventPayload)]);
    }

    private static ValueTask<KindResult> HandleDeclineAsync(NeuronContext context, string inputJson)
    {
        var root = ParseJson(inputJson);
        var sourceHash = RequireString(root, "sourceHash");
        var reason = RequireString(root, "reason");

        var folded = Fold(context.Journal);
        if (folded.State != "proposed" || folded.ActiveHash != sourceHash)
        {
            throw new BrainException("input.invalid", "no matching proposal for sourceHash");
        }

        var eventPayload = JsonSerializer.Serialize(new { sourceHash, reason }, JsonOptions);
        var output = JsonSerializer.Serialize(new { status = "declined" }, JsonOptions);

        return ValueTask.FromResult(new KindResult(output, [("behavior.declined", eventPayload)]));
    }

    private async ValueTask<KindResult> HandleRollbackAsync(NeuronContext context, NeuronInvocation invocation)
    {
        var root = ParseJson(invocation.InputJson);
        var sourceHash = RequireString(root, "sourceHash");

        var folded = Fold(context.Journal);
        if (!folded.GrantsByHash.TryGetValue(sourceHash, out var grants) ||
            !folded.History.Any(entry => entry.Hash == sourceHash && entry.State == "enabled"))
        {
            throw new BrainException("input.invalid", "sourceHash was never enabled");
        }

        var identity = await IssueGrantsAsync(context, sourceHash, grants, $"rollback:{invocation.CommandId}");

        var grantsJson = JsonSerializer.Serialize(grants, JsonOptions);
        var eventPayload = JsonSerializer.Serialize(new { sourceHash, grantsJson }, JsonOptions);
        var output = JsonSerializer.Serialize(new { status = "enabled", identity }, JsonOptions);

        return new KindResult(output, [("behavior.rolledback", eventPayload)]);
    }

    private async Task<string> IssueGrantsAsync(NeuronContext context, string sourceHash, BehaviorGrant[] grants, string tag)
    {
        var granteeKey = IdentityKey(context.Address.OwnerId, sourceHash);
        var targetAddresses = new NeuronAddress[grants.Length];

        for (var i = 0; i < grants.Length; i++)
        {
            ValidateGrantContract(grants[i].Contract);
            targetAddresses[i] = ParseGrantAddress(grants[i].Address);
        }

        for (var i = 0; i < grants.Length; i++)
        {
            var target = grainFactory.GetGrain<INeuron>(targetAddresses[i].ToGrainKey());
            var grantInputJson = JsonSerializer.Serialize(new { granteeKey, contract = grants[i].Contract }, JsonOptions);
            var commandId = $"{context.Address.ToGrainKey()}:{tag}:{sourceHash}:{i}";
            _ = await target.InvokeAsync(new NeuronInvocation("neuron.grant.v1", grantInputJson, commandId, context.Address.ToGrainKey()));
        }

        return granteeKey;
    }

    private static string IdentityKey(string ownerId, string sourceHash) =>
        new NeuronAddress(ownerId, $"behavior/{sourceHash}", $"behavior/{sourceHash}").ToGrainKey();

    private static BehaviorGrant[] ParseGrants(JsonElement root)
    {
        if (!root.TryGetProperty("grants", out var grantsElement) || grantsElement.ValueKind != JsonValueKind.Array)
        {
            throw new BrainException("input.invalid", "grants field is required");
        }

        if (grantsElement.GetArrayLength() > MaxGrants)
        {
            throw new BrainException("input.invalid", $"grants exceeds maximum of {MaxGrants}");
        }

        var grants = new List<BehaviorGrant>();
        foreach (var grantElement in grantsElement.EnumerateArray())
        {
            if (grantElement.ValueKind != JsonValueKind.Object)
            {
                throw new BrainException("input.invalid", "each grant must be an object with address and contract");
            }

            var address = RequireString(grantElement, "address");
            var contract = RequireString(grantElement, "contract");
            ValidateGrantContract(contract);
            ParseGrantAddress(address);
            grants.Add(new BehaviorGrant(address, contract));
        }

        return [.. grants];
    }

    private static NeuronAddress ParseGrantAddress(string address)
    {
        try
        {
            return NeuronAddress.Parse(address);
        }
        catch (ArgumentException)
        {
            throw new BrainException("input.invalid", $"malformed grant address '{address}'");
        }
    }

    private static void ValidateGrantContract(string contract)
    {
        if (contract.StartsWith("behavior.", StringComparison.Ordinal) ||
            contract.StartsWith("neuron.grant", StringComparison.Ordinal) ||
            contract.StartsWith("neuron.revoke", StringComparison.Ordinal) ||
            contract.StartsWith("effect.", StringComparison.Ordinal))
        {
            throw new BrainException("input.invalid", "governance contracts are not grantable");
        }
    }

    private static string ComputeGrantsHash(BehaviorGrant[] grants) => Sha256Hex(CanonicalGrantsJson(grants));

    private static string CanonicalGrantsJson(BehaviorGrant[] grants) => JsonSerializer.Serialize(
        grants.OrderBy(g => g.Address, StringComparer.Ordinal).ThenBy(g => g.Contract, StringComparer.Ordinal).ToArray(),
        JsonOptions);

    private readonly record struct HistoryEntry(string Hash, string State);

    private sealed record FoldedJournal(
        string State,
        string? ActiveHash,
        BehaviorGrant[] Grants,
        IReadOnlyList<HistoryEntry> History,
        IReadOnlyDictionary<string, BehaviorGrant[]> GrantsByHash);

    private sealed record ProposedPayload(string SourceHash, string GrantsJson, bool BddPassed);
    private sealed record EnabledPayload(string SourceHash, string GrantsJson);
    private sealed record DeclinedPayload(string SourceHash, string Reason);

    private static FoldedJournal Fold(IReadOnlyList<NeuronEvent> journal)
    {
        var history = new List<HistoryEntry>();
        var grantsByHash = new Dictionary<string, BehaviorGrant[]>();
        var state = "none";
        string? activeHash = null;

        foreach (var evt in journal)
        {
            switch (evt.Kind)
            {
                case "behavior.proposed":
                    {
                        var payload = JsonSerializer.Deserialize<ProposedPayload>(evt.PayloadJson, JsonOptions)!;
                        grantsByHash[payload.SourceHash] = DeserializeGrants(payload.GrantsJson);
                        state = "proposed";
                        activeHash = payload.SourceHash;
                        history.Add(new HistoryEntry(payload.SourceHash, "proposed"));
                        break;
                    }
                case "behavior.enabled" or "behavior.rolledback":
                    {
                        var payload = JsonSerializer.Deserialize<EnabledPayload>(evt.PayloadJson, JsonOptions)!;
                        grantsByHash[payload.SourceHash] = DeserializeGrants(payload.GrantsJson);
                        state = "enabled";
                        activeHash = payload.SourceHash;
                        history.Add(new HistoryEntry(payload.SourceHash, "enabled"));
                        break;
                    }
                case "behavior.declined":
                    {
                        var payload = JsonSerializer.Deserialize<DeclinedPayload>(evt.PayloadJson, JsonOptions)!;
                        state = "declined";
                        activeHash = payload.SourceHash;
                        history.Add(new HistoryEntry(payload.SourceHash, "declined"));
                        break;
                    }

                default:
                    break;
            }
        }

        var grants = activeHash is not null && grantsByHash.TryGetValue(activeHash, out var activeGrants) ? activeGrants : [];
        return new FoldedJournal(state, activeHash, grants, history, grantsByHash);
    }

    private static BehaviorGrant[] DeserializeGrants(string grantsJson) =>
        JsonSerializer.Deserialize<BehaviorGrant[]>(grantsJson, JsonOptions) ?? [];

    private static JsonElement ParseJson(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            return doc.RootElement.Clone();
        }
    }

    private static string RequireString(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new BrainException("input.invalid", $"{field} field is required");
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new BrainException("input.invalid", $"{field} cannot be empty") : value;
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
