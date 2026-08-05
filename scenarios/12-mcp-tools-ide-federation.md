# Scenario 12: MCP tools federation from an external IDE

## User intent
A developer owner uses an IDE MCP client to ask DigitalBrain for active neurons, read a chat transcript, and trigger a safe introspection tool — the same northbound MCP surface the product AppHost exposes, federated with the owner's running brain modules.

## Trigger
External MCP client JSON-RPC tool call (e.g. `list_active_neurons`, `read_chat_transcript`) against digitalbrain-mcp / silo northbound.

## Imagined modules
- MCP Northbound (tool catalog, auth)
- Introspection
- Chat
- Behaviors
- Security/OAuth (owner session)
- Assistant (optional reverse: model selects MCP-published tools)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| mcpgateway/owner | Map MCP tools ↔ synapse asks |
| introspection/default | Answer topology/journal queries |
| chat/owner-desk | Transcript reads |
| behaviors/catalog | List/activate behaviors via tools |
| security/owner-session | Token → owner binding |
| assistant/owner | May call outbound MCP tools in other scenarios |

## Synapse choreography
1. MCP request authenticated → gateway **broadcasts** `McpToolInvoked` (tool, argsHash, clientId) — no secrets in body.
2. Gateway **directs** typed ask e.g. `ActiveNeuronsAsked` → introspection → `ActiveNeuronsAnswered`.
3. Gateway maps answer → MCP tool result; **broadcasts** `McpToolCompleted` (ok, duration).
4. Transcript path: `ChatTranscriptAsked` → chat neuron → `ChatTranscriptAnswered` (redacted per policy).
5. If tool is mutating (activate behavior): insert `ApprovalRequired` unless token has elevated scope.
6. IDE displays results; optional second tool `read_neuron_journal` → `JournalPageAsked` / `JournalPageAnswered`.
7. All under one owner request context so journals correlate IDE session activity.

## Orleans / Core surface exercised
Grain call filters (authz); request context; DurableGrain journals; module catalog as tool reflection source; stateless workers optional on gateway; serialized turns on target neurons; outbox; no transactions.

## Rich experience
IDE side: tool results as structured tables. Shell: "External MCP session active" badge listing recent `McpToolInvoked`. Audit pane of tool calls.

## Failure / adversarial cases
- Token confused deputy: gateway must bind owner from token, never from tool args ownerId.
- Tool enumerates other owners' neurons → fail closed.
- Large journal page: hard limit + cursor; no unbounded read.
- Mutating tool without approval scope → `McpToolDenied`.
- Replay of MCP requests: idempotency keys on mutating tools.

## Capability claim
The brain's synapses are operable from federated MCP clients with the same owner-scoped journal truth as the first-party shell — the OS is a programmable substrate, not a trapped chat UI.
