using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestOwner
{
    private TestOwner(OwnerId id, IDigitalBrain client)
    {
        Id = id;
        Client = client;
    }

    public OwnerId Id { get; }

    public IDigitalBrain Client { get; }

    internal static TestOwner Create(FixtureCluster cluster, OwnerId id)
        => new(id, DigitalBrainClient.Connect(cluster.Client, id.Value));
}

internal static class IdentityLabel
{
    internal static string Validate(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (label.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Owner labels cannot contain '/'.",
                nameof(label));
        }

        if (label.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Owner labels cannot contain whitespace.",
                nameof(label));
        }

        return label;
    }
}
