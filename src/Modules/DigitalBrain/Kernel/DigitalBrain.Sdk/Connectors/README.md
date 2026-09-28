# Connectors

`ConnectorModule` stores a per-owner list of external-source credentials using SDK Secrets.
Other modules can use `IConnectors` without depending on IntoChat. Sources are free-form strings;
the consuming product chooses which ones to offer.

The default probe checks only whether a credential exists. A host may register an
`IConnectorProbe` that verifies a source. A `Connected` result from the default probe does
not establish that the external service accepted the credential.

The HTTP path and Orleans aliases remain `connections` so existing clients and persisted
records continue to work after the move.
