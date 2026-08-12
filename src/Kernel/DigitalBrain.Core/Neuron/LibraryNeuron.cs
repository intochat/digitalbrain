using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

[GrainType(ILibrary.GrainTypeName)]
public sealed class LibraryNeuron : Neuron, ILibrary
{
    private const string StateName = "library.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDurableValue<string> _json;

    public LibraryNeuron()
    {
        _json = ServiceProvider.GetRequiredKeyedService<IDurableValue<string>>(StateName);
    }

    public Task HandleAsync(PublishLibraryArtifact synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var publisher = synapse.Publisher
            ?? VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Library '{Id}' refuses publish without a verified publisher.");

        if (string.IsNullOrWhiteSpace(synapse.Name)
            || string.IsNullOrWhiteSpace(synapse.Version)
            || string.IsNullOrWhiteSpace(synapse.StructureJson))
        {
            throw new NeuronAuthorizationException(
                "Publish requires Name, Version, and StructureJson.");
        }

        var state = Load();
        var name = synapse.Name.Trim();
        var version = synapse.Version.Trim();
        if (state.Artifacts.Any(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Version, version, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NeuronAuthorizationException(
                $"Library already has immutable artifact '{name}@{version}'. Publish a new version.");
        }

        var structure = synapse.StructureJson.Trim();
        var hash = ContentHash(structure);
        var artifactId = $"{name.ToLowerInvariant()}-{version}-{hash[..12]}";
        var artifact = new LibraryArtifact(
            artifactId,
            name,
            version,
            string.IsNullOrWhiteSpace(synapse.Description) ? name : synapse.Description.Trim(),
            hash,
            structure,
            publisher.PrincipalId,
            TimeProvider.GetUtcNow());

        state.Artifacts.Add(artifact);
        Save(state);
        return ReplyAsync(new LibraryArtifactPublished(synapse.CommandId, artifact), cancellationToken);
    }

    public Task HandleAsync(DiscoverLibrary synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var intent = (synapse.Intent ?? "").Trim();
        var limit = synapse.Limit <= 0 ? 8 : Math.Min(synapse.Limit, 32);
        var tokens = intent.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var artifacts = Load().Artifacts;
        var hits = artifacts
            .Select(a => (Artifact: a, Score: Score(a, tokens, intent)))
            .Where(x => x.Score > 0 || tokens.Length == 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Artifact.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Artifact)
            .ToArray();

        if (hits.Length == 0 && artifacts.Count > 0)
        {
            hits = [.. artifacts.OrderByDescending(a => a.PublishedAt).Take(limit)];
        }

        return ReplyAsync(new LibraryDiscoveries(synapse.CommandId, hits), cancellationToken);
    }

    public async Task HandleAsync(InstallLibraryArtifact synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var installer = synapse.Installer
            ?? VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Library '{Id}' refuses install without a verified installer.");

        var state = Load();
        var artifact = state.Artifacts.FirstOrDefault(a =>
                string.Equals(a.ArtifactId, synapse.ArtifactId, StringComparison.OrdinalIgnoreCase))
            ?? throw new NeuronAuthorizationException(
                $"Unknown artifact '{synapse.ArtifactId}'. Discover first.");

        var installId = $"{installer.PrincipalId.Value:N}.{artifact.ArtifactId}";
        if (state.Installs.Any(i =>
                string.Equals(i.InstallId, installId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new NeuronAuthorizationException(
                $"Principal already installed '{artifact.Name}@{artifact.Version}'.");
        }

        var install = new LibraryInstall(
            installId,
            artifact.ArtifactId,
            artifact.Name,
            artifact.Version,
            artifact.ContentHash,
            installer.PrincipalId,
            Enabled: false,
            ConfigJson: null,
            TimeProvider.GetUtcNow(),
            EnabledAt: null);

        state.Installs.Add(install);
        Save(state);

        // Arriving disabled: copy structure into principal registry with Enabled=false.
        var registryId = PrincipalRegistryId(installer.PrincipalId);
        var bundle = $"lib:{artifact.Name}@{artifact.Version}";
        foreach (var member in ParseMembers(artifact.StructureJson, installer.PrincipalId))
        {
            var subject = new NeuronId(member.GrainType, Id.Owner, member.Name);
            await SendAsync(
                registryId,
                new RegisterInstance(
                    CommandId.New(),
                    subject,
                    member.Role,
                    Bundle: bundle,
                    Enabled: false,
                    Note: $"artifact:{artifact.ArtifactId} hash={artifact.ContentHash}"))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await FlushOutboxAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(new LibraryInstallRecorded(synapse.CommandId, install), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ListLibraryInstalls synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var actor = synapse.Actor ?? VerifiedActor.Current;
        var installs = Load().Installs.AsEnumerable();
        if (actor is not null)
        {
            installs = installs.Where(i => i.Installer == actor.PrincipalId);
        }

        return ReplyAsync(
            new LibraryInstallsListed(synapse.CommandId, [.. installs.OrderBy(i => i.Name)]),
            cancellationToken);
    }

    public async Task HandleAsync(EnableLibraryInstall synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var actor = synapse.Actor
            ?? VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Library '{Id}' refuses enable without a verified principal.");

        var state = Load();
        var index = state.Installs.FindIndex(i =>
            string.Equals(i.InstallId, synapse.InstallId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new NeuronAuthorizationException($"Unknown install '{synapse.InstallId}'.");
        }

        var install = state.Installs[index];
        if (install.Installer != actor.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                "Only the installing principal may enable this install.");
        }

        install = install with
        {
            Enabled = true,
            ConfigJson = string.IsNullOrWhiteSpace(synapse.ConfigJson)
                ? install.ConfigJson
                : synapse.ConfigJson.Trim(),
            EnabledAt = TimeProvider.GetUtcNow(),
        };
        state.Installs[index] = install;
        Save(state);

        var artifact = state.Artifacts.FirstOrDefault(a => a.ArtifactId == install.ArtifactId);
        if (artifact is not null)
        {
            var registryId = PrincipalRegistryId(actor.PrincipalId);
            foreach (var member in ParseMembers(artifact.StructureJson, actor.PrincipalId))
            {
                var subject = new NeuronId(member.GrainType, Id.Owner, member.Name);
                await SendAsync(
                    registryId,
                    new SetInstanceEnabled(CommandId.New(), subject, Enabled: true))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            }

            await FlushOutboxAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await ReplyAsync(new LibraryInstallEnabled(synapse.CommandId, install), cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private NeuronId PrincipalRegistryId(PrincipalId principal)
        => new(
            IRegistry.GrainTypeName,
            Id.Owner,
            PrincipalPartition.InstanceName(principal, IRegistry.InstanceName));

    private static BundleMember[] ParseMembers(string structureJson, PrincipalId principal)
    {
        try
        {
            using var doc = JsonDocument.Parse(structureJson);
            if (!doc.RootElement.TryGetProperty("members", out var members)
                || members.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<BundleMember>();
            foreach (var m in members.EnumerateArray())
            {
                var grainType = m.TryGetProperty("grainType", out var gt) ? gt.GetString() : null;
                var local = m.TryGetProperty("localName", out var ln) ? ln.GetString()
                    : m.TryGetProperty("name", out var n) ? n.GetString() : null;
                var role = m.TryGetProperty("role", out var r) ? r.GetString() : "cell";
                var note = m.TryGetProperty("note", out var nt) ? nt.GetString() : null;
                if (string.IsNullOrWhiteSpace(grainType) || string.IsNullOrWhiteSpace(local))
                {
                    continue;
                }

                list.Add(new BundleMember(
                    grainType.Trim(),
                    PrincipalPartition.InstanceName(principal, local.Trim()),
                    role?.Trim() ?? "cell",
                    note));
            }

            return [.. list];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int Score(LibraryArtifact artifact, string[] tokens, string intent)
    {
        if (tokens.Length == 0)
        {
            return 1;
        }

        var hay = $"{artifact.Name} {artifact.Description} {artifact.Version}".ToLowerInvariant();
        var score = 0;
        foreach (var token in tokens)
        {
            if (hay.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
            {
                score += 2;
            }
        }

        if (!string.IsNullOrWhiteSpace(intent)
            && hay.Contains(intent.ToLowerInvariant(), StringComparison.Ordinal))
        {
            score += 5;
        }

        return score;
    }

    private static string ContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private LibraryState Load()
    {
        var text = _json.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LibraryState();
        }

        return JsonSerializer.Deserialize<LibraryState>(text, JsonOptions) ?? new LibraryState();
    }

    private void Save(LibraryState state)
        => _json.Value = JsonSerializer.Serialize(state, JsonOptions);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A library command requires a command id.");
        }
    }

    private sealed class LibraryState
    {
        public List<LibraryArtifact> Artifacts { get; set; } = [];

        public List<LibraryInstall> Installs { get; set; } = [];
    }
}
