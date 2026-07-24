# Centralized Runtime Design

## Decision

DigitalBrain keeps domain vocabulary in independently shipped modules, but protocol and durability
mechanics are shared infrastructure. Google and Salesforce must not each implement OAuth, encrypted
token persistence, MCP session lifecycle, schema canonicalization, or JSON result handling. AI must
not own a private process-local encryption key ring for state that Orleans promises to recover.

The implementation therefore has four seams:

1. `DigitalBrain.Aspire.Hosting` owns the brain application model: the Orleans resource, the durable
   Azure Storage profile, journal projection and readiness, and one secret state-protection key.
2. `DigitalBrain.Security` owns purpose-bound authenticated encryption for durable module payloads.
3. `DigitalBrain.Integrations.Mcp` owns the official MCP C# SDK adapter, OAuth client construction,
   token caching, catalog admission, schema fingerprints, and typed structured-content failures.
4. Provider modules own their semantic neuron interface, provider endpoint and scopes, admitted tool
   contracts, payload mapping, and domain-specific mutation policy.

All three infrastructure interfaces are internal module-author seams. They do not enter contract
packages, generated catalogs, behavior compilation, or the client programming model.

## Aspire and Orleans

`BrainService.WithAzureStorage(storage)` is the complete durable profile. It derives separate Table
resources for Orleans clustering and reminders plus a Blob resource named `journal`, configures the
Orleans application model, and projects the journal connection and readiness dependency whenever a
silo references the brain. AppHosts no longer bolt a journal reference beside
`WithDevelopmentStores()`.

Modules that persist confidential payloads call one module-author hook during AppHost composition.
The hook creates one secret parameter per brain and projects it only through `WithReference(brain)`;
`AsClient()` never receives it. The runtime accepts exactly one base64-encoded 256-bit key. Missing,
malformed, or changed keys fail closed instead of making durable state appear recoverable when it is
not.

Orleans remains the durability authority. Module state is still committed through journaling before
external effects. The security module only protects opaque bytes; it does not add a second store,
transaction log, or state lifecycle.

## MCP and authentication

`IMcpClientFactory` creates an activation-local `IMcpClient` from a provider definition and the
neuron's durable token slot. The client exposes only two operations: inspect one required tool, and
invoke that exact tool while re-admitting its current schema against the previously stored
fingerprint. The production adapter uses `HttpClientTransport` and `McpClient`; provider assemblies
contain no transport DTOs or copied client lifecycle.

A provider definition supplies the official endpoint, display name, configuration root, OAuth
scopes, and token-protection purpose. A tool contract supplies its exact name, required input
properties, and effect expectations. Google and Salesforce therefore remain responsible for what
they trust without reimplementing how trust is enforced.

OAuth tokens are per-neuron, encrypted with the brain-wide key under a provider-and-owner-specific
purpose, and committed through the neuron's journal. The SDK owns discovery, PKCE, refresh, and token
exchange. A local browser callback adapter is explicitly a development adapter. It validates the
redirect path and OAuth state before returning a code. Production authorization belongs at an
authenticated edge and must provide another adapter; a silo must never pretend that launching its
own browser is a hosted authorization flow.

## Provider behavior

Gmail keeps `IGmail.ReadMessage` and maps the official Developer Preview `get_message` result
into `GmailMessage`. Its policy admits only the read-only, non-destructive tool with required
`messageId` input and requests only the Gmail read-only scope.

Salesforce uses the official `platform/sobject-mutations` hosted server. A proposal validates and
durably records intent without network access. Approval validates exact durable human evidence,
inspects the current `update_sobject_record` and `soqlQuery` contracts, commits the `Invoking` fence,
and only then calls the mutation. Recovery queries provider state and never repeats an uncertain
write. No code claims exactly-once external effects.

## AI and Microsoft Agent Framework

MAF owns agents, group chat, concurrent workflows, sessions, execution, and checkpoints. DigitalBrain
keeps only the adapters required to address typed Orleans neurons and the checkpoint store required
to persist MAF's JSON checkpoint contract in Orleans.

Direct MAF sessions and workflow checkpoints use `DigitalBrain.Security`, so state survives process
restart and another silo can decrypt it when given the same brain key. The unused `Agent` base and
`MafAgentFactory` are removed because their capability collection has no consumer and creates no MAF
tools. The three equivalent neuron-to-`IChatClient` implementations become one adapter.

No MAF Durable Extension or second workflow engine is introduced.

## Verification

Tests exercise the interfaces, not provider copies:

- authenticated encryption survives provider recreation and rejects a different key or purpose;
- the durable Aspire profile wires clustering, reminders, journal projection, readiness, and
  silo-only secrets once;
- one official MCP adapter lists, admits, fingerprints, invokes, cancels, and parses structured
  content for both provider policies;
- proposals do not contact Salesforce, while approved mutations retain the existing durable fence
  and reconciliation proofs;
- MAF session and checkpoint restart simulations remain green with the stable protector;
- package-boundary guards keep MCP and security infrastructure off every consumer path;
- the root `dotnet test --logger "console;verbosity=minimal"` gate passes.
