# Integration accounts

`IntegrationsModule` stores a per-owner list of accounts on external integrations using SDK Secrets.
`IIntegrationAccounts` is platform-only: callers come from the ambient stamp, and users reach it through
`/brains/{brainId}/integrations/accounts`. Integrations are free-form ids; the consuming product chooses
which ones to offer. Modules with a grant that lives outside the registry (an OAuth neuron) contribute
an `IExternalAccount`.

The default probe checks only whether a credential exists. A host may register an `IAccountProbe`
that verifies an integration. A `Connected` result from the default probe does not establish that the
external service accepted the credential.

Orleans aliases stay `connections*` (persisted); only C# names and HTTP routes were renamed.
