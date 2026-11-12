namespace DigitalBrain.Capabilities.Connections.Connectors;

using DigitalBrain.Brains;
using DigitalBrain.Contracts;
using DigitalBrain.Rules;

/// <summary>Offers and capabilities provided by declared connectors.</summary>
public interface IConnectorOffers
{
    /// <summary>All connectors declared in the host environment.</summary>
    IReadOnlyList<ConnectorDefinition> Declared { get; }

    /// <summary>Connectors currently offered for connection.</summary>
    IReadOnlyList<ConnectorDefinition> Offered { get; }

    /// <summary>Capability identifiers provided by a connector.</summary>
    IReadOnlyList<CapabilityId> ProvidesOf(string connectorId);
}

/// <summary>Default implementation of <see cref="IConnectorOffers"/>.</summary>
public sealed class DefaultConnectorOffers : IConnectorOffers
{
    private readonly IReadOnlyList<ConnectorDefinition> _connectors;
    private readonly ConnectionNeuronTypes? _neuronTypes;

    /// <summary>Creates a new connector offers instance.</summary>
    public DefaultConnectorOffers(
        IReadOnlyList<ConnectorDefinition>? connectors = null,
        ConnectionNeuronTypes? neuronTypes = null)
    {
        _connectors = connectors ?? [];
        _neuronTypes = neuronTypes;
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectorDefinition> Declared => _connectors;

    /// <inheritdoc />
    public IReadOnlyList<ConnectorDefinition> Offered => _connectors;

    /// <inheritdoc />
    public IReadOnlyList<CapabilityId> ProvidesOf(string connectorId)
        => _neuronTypes?.ProvidesOf(connectorId) ?? [];
}

internal sealed record IntegrationIsAvailable(string Kind, bool Available) : Rule("identity.integration-available")
{
    public override bool Broken => !Available;
    public override string Message => $"Connector '{Kind}' is not available on this host.";
}
