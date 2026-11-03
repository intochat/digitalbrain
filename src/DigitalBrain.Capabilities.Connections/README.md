# DigitalBrain.Capabilities.Connections

The `connections@1` capability contract: connectors, integrations, connections, connection requests, capability broker bindings and secret sealing.

- `ConnectorDefinition`, `OAuthMethod`, `OAuthExternalOperations`, `IOAuthTokenExchange`, `OAuthTokenFailure`, `OAuthTokenExchangeException`, `IntegrationSchema`, `HostConfiguration` (`DigitalBrain.Capabilities.Connections.Connectors`).
- `IModuleIntegrations<TModule>`, `IIntegrationHandle`, `IIntegrationStartup`, `IntegrationSnapshot`, `IntegrationStatus`, `ConfigureIntegration`, `ReleasedIntegration`, `ICapabilitySource`, `ICapabilities`, `IntegrationCapability`, `IRuntimeCapabilitySource`, `NeuronType`, `NeuronTypeHit` (`DigitalBrain.Capabilities.Connections.Integrations`).
- `IConnection`, `IConnectionInbound`, `ConnectionStatus`, `ConnectionView`, `OAuthGrant`, `Credential`, `ConnectionProbeOutcome`, `ConnectionProbeResult`, `IBrainOwnerDispatch`, `ConnectionKeys`, `ConnectionState`, `ConnectionConnected`, `ConnectionExpired`, `ConnectionFailing`, `ConnectionDisconnected`, `ConnectionUpdated`, `IConnectionIndex`, `IConnectionCatalogUpdates`, `IConnectionWriteFaults` (`DigitalBrain.Capabilities.Connections`).
- `IConnectionRequests`, `ConnectionRef`, `ConnectorOffer`, `ConnectorKind`, `ConnectionSummary`, `ConnectionRequested`, `ConnectionsChanged` (`DigitalBrain.Capabilities.Connections.Requests`).
- `ICapabilityConnections`, `BrainConnection`, `ICapabilityBindingInterceptor` (`DigitalBrain.Capabilities.Connections.Broker`).
- `ISecretSealer`, `IBrainSecretSealer`, `BrainSeal` (`DigitalBrain.Capabilities.Connections.Sealing`).

References only `DigitalBrain`, `DigitalBrain.Capabilities.Identity`, and `DigitalBrain.Capabilities.Secrets`; no Orleans, no ASP.NET Core.
