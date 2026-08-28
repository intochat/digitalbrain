# Experience Learning with Protocol-Real MCP Fakes

## Outcome

DigitalBrain exposes one product concept, **Experience**. An Experience is implemented by the existing Behavior runtime, while its immutable revisions are internal Gherkin features. Users create, run, and correct Experiences only through chat. “Smart Prompt” remains a presentation style and is not a second persisted aggregate.

The first vertical slice is `salesforce-account-enrichment`: receive an email address, research its company, find or update the matching Salesforce Account, and report the result. Development uses protocol-real Gmail and Salesforce MCP servers with deterministic data. Production can replace their endpoints and authentication without changing Experience code.

## Product Flow

1. The assistant lists `salesforce-account-enrichment` as an Experience.
2. “Run the Salesforce account enrichment experience for vlad@intochat.io” invokes the active Gherkin revision.
3. The runner derives `intochat.io`, uses web research, and calls Salesforce through a real `ModelContextProtocol.Client.McpClient` session.
4. Development calls the fake MCP resource; responses have the same tool names and input shapes as the official remote servers.
5. “Do it differently: preserve verified Salesforce fields” is captured as learning evidence.
6. A candidate Gherkin revision adds a focused rule and paired regression scenario.
7. The new regression must fail against the parent plan and the complete candidate suite must pass.
8. The candidate activates atomically; the parent remains available for rollback.
9. A second company run uses the active learned revision without another reminder.

## Domain Model

`IBehaviorDefinition` remains the compatibility interface. User-facing tools and copy call it an Experience.

`BehaviorDefinitionState` keeps its current candidate-facing fields for API compatibility and adds:

- immutable `BehaviorRevision` values keyed by source hash;
- `ActiveRevisionHash`;
- immutable `LearningEvidence` values.

Saving appends a candidate revision and never disables the active revision. Testing attaches a report to the candidate. Activation swaps subscriptions from the previous active revision to the green candidate. The runner resolves the exact subscribed revision by hash, falling back to legacy state for existing persisted records.

## BDD Learning Gate

The compiler remains deterministic and accepts only registered bindings. The account-enrichment vocabulary adds:

- enrich Salesforce Account from the email sender using web research;
- preserve verified Salesforce fields;
- fake company email input;
- assert that an Account enrichment is proposed;
- assert that verified fields are preserved.

`BehaviorTestInterpreter` gains candidate-vs-parent regression validation. A learning candidate is rejected unless at least one new candidate test fails on the parent and all candidate tests pass on the candidate. This is specification-level red-green proof; runtime integration tests separately execute the active scenario through real protocol clients and fake MCP servers.

## MCP Boundary

The fake server is an ASP.NET Core MCP application started twice by Aspire:

- `fake-gmail-mcp`, exposing the official Gmail Developer Preview catalog: `create_draft`, `get_message`, `get_thread`, `label_message`, `label_thread`, `list_drafts`, `list_labels`, `search_threads`, `unlabel_message`, and `unlabel_thread`.
- `fake-salesforce-mcp`, exposing the Salesforce SObject Mutations tools used by the official server: `getObjectSchema`, `soqlQuery`, `soslSearch`, `createRecord`, `updateRecord`, and `updateRelatedRecord`.

Tool responses use `StructuredContent`; data is deterministic and held in process memory. The fake Salesforce Account for `intochat.io` has a verified Description so the learned preservation rule can be demonstrated.

The Integration module receives endpoint URLs through configuration. `McpIntegrationClient` creates a real Streamable HTTP `HttpClientTransport` and `McpClient`, discovers the live tool catalog, refuses unknown tools, calls the selected tool, and returns structured JSON. Existing Gmail and Salesforce transport interfaces become small adapters over this generic client. Simulation mode without configured endpoints retains the current in-process fakes.

## Agent and Chat Boundary

`BehaviorToolSource` publishes user-facing tools named:

- `list_experiences`;
- `run_experience`;
- `learn_experience`.

The assistant instructions define an explicit correction as a learning signal and require `learn_experience` after a user corrects the result of an Experience. General conversation is never treated as learning evidence.

The deterministic testing chat client scripts create/run/learn requests so simulation and E2E tests exercise the same `IAgentToolSource` and function-invocation middleware used by a real model.

## Safety and Failure Behavior

- Unknown MCP tools fail closed after live discovery.
- Missing or malformed structured content fails the Experience run; text is not silently reinterpreted as JSON.
- Candidate compilation or tests failing leaves the active revision unchanged.
- A correction that creates no failing regression is rejected as unproven.
- Existing owner scoping is retained for Experience state and evidence.
- Development fakes may report a successful fake mutation. Replacing the endpoint with a real Salesforce MCP server still requires the product’s generic approval policy before destructive tools; the POC does not claim that OAuth/approval rail is complete.

## Verification

- Unit tests freeze official fake tool catalogs and deterministic schemas.
- Protocol tests use a real `McpClient` against each fake HTTP server.
- Simulation tests prove immutable revisions, failed-candidate isolation, red-green learning, and cross-company transfer.
- Chat tests prove the assistant receives and invokes Experience tools.
- Aspire E2E starts both fake MCP resources, waits for health, runs the Experience through chat, and observes the fake Salesforce result.

## Sources

- Salesforce SObject Mutations: https://developer.salesforce.com/docs/platform/hosted-mcp-servers/guide/sobject-mutations.html
- Gmail MCP reference: https://developers.google.com/workspace/gmail/api/reference/mcp
