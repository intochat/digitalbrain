using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class RegistryTools(IDigitalBrain brain)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = McpSurface.ListRegistry)]
    [Description(
        "List durable registered neuron instances for the owner, including cold and disabled ones. "
        + "This is the Wave 2 catalog — not the same as live activations.")]
    public async Task<IReadOnlyList<RegistryEntry>> ListRegistryAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Bound);

        var listed = await brain
            .Get<IRegistry>(IRegistry.InstanceName)
            .FireAsync<InstancesListed>(new ListInstances(CommandId.New()), timeout.Token)
            .ConfigureAwait(false);

        return
        [
            .. listed.Items
                .OrderBy(static e => e.Subject.ToString(), StringComparer.Ordinal)
                .Select(static e => new RegistryEntry(
                    e.Subject.ToString(),
                    e.Role,
                    e.Bundle,
                    e.Enabled,
                    e.Note,
                    e.RegisteredAt)),
        ];
    }

    [McpServerTool(Name = McpSurface.RegisterInstance)]
    [Description(
        "Register a neuron instance in the durable registry so it appears when cold. "
        + "identity is type:name or type:owner/name (e.g. chart:sales-cold, timer:dev/nightly).")]
    public async Task<RegistryEntry> RegisterInstanceAsync(
        [Description("Neuron identity: type:name or type:owner/name")] string identity,
        [Description("Role label: chart, schedule, timer, cell, …")] string role,
        [Description("Optional bundle name if this membership is part of a bundle")] string? bundle = null,
        [Description("Whether the instance is enabled")] bool enabled = true,
        [Description("Optional note")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var subject = ParseIdentity(identity, brain.Owner);
        var registered = await brain
            .Get<IRegistry>(IRegistry.InstanceName)
            .FireAsync<InstanceRegistered>(
                new RegisterInstance(CommandId.New(), subject, role.Trim(), bundle, enabled, note),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var instance = registered.Instance;
        return new RegistryEntry(
            instance.Subject.ToString(),
            instance.Role,
            instance.Bundle,
            instance.Enabled,
            instance.Note,
            instance.RegisteredAt);
    }

    [McpServerTool(Name = McpSurface.InstallBundle)]
    [Description(
        "Install a named bundle as disabled members in one request. "
        + "membersJson is a JSON array of {grainType,name,role,note?}.")]
    public async Task<BundleInstallResult> InstallBundleAsync(
        [Description("Bundle name")] string name,
        [Description("JSON array of members: [{grainType,name,role,note?},…]")] string membersJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(membersJson);

        var members = System.Text.Json.JsonSerializer.Deserialize<BundleMemberDto[]>(
                membersJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("membersJson must be a JSON array.");

        if (members.Length == 0)
        {
            throw new ArgumentException("Bundle needs at least one member.");
        }

        var installed = await brain
            .Get<IRegistry>(IRegistry.InstanceName)
            .FireAsync<BundleInstalled>(
                new InstallBundle(
                    CommandId.New(),
                    name.Trim(),
                    [.. members.Select(static m => new BundleMember(
                        m.GrainType ?? throw new ArgumentException("grainType required"),
                        m.Name ?? throw new ArgumentException("name required"),
                        m.Role ?? throw new ArgumentException("role required"),
                        m.Note))],
                    Wires: [],
                    Intent: $"mcp install_bundle '{name.Trim()}'"),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return new BundleInstallResult(
            installed.Name,
            installed.MemberCount,
            installed.WireCount,
            installed.Enabled);
    }

    private static NeuronId ParseIdentity(string identity, OwnerId owner)
    {
        var separator = identity.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == identity.Length - 1)
        {
            throw new ArgumentException(
                "identity must be type:name or type:owner/name.",
                nameof(identity));
        }

        var type = identity[..separator];
        var rest = identity[(separator + 1)..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            return new NeuronId(type, new OwnerId(rest[..slash]), rest[(slash + 1)..]);
        }

        return new NeuronId(type, owner, rest);
    }

    private sealed record BundleMemberDto(string? GrainType, string? Name, string? Role, string? Note);
}

internal sealed record RegistryEntry(
    string Identity,
    string Role,
    string? Bundle,
    bool Enabled,
    string? Note,
    DateTimeOffset RegisteredAt);

internal sealed record BundleInstallResult(
    string Name,
    int MemberCount,
    int WireCount,
    bool Enabled);
