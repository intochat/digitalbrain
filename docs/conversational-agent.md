# Conversational agent

The default Flutter shell talks directly to a single Microsoft Agent Framework agent through `POST /agent`. The endpoint uses `AddAGUIServer` / `MapAGUIServer` from `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.20.0-preview.260831.1. Conversation orchestration does not schedule chat-neuron signals or read turn journals; table tools persist data through UI neurons.

## Available now

- Streamed Markdown responses, including clickable citations.
- Server-owned conversation history between messages.
- The `search_web` function backed by Tavily, with visible progress and source cards.
- Stop a response by cancelling its HTTP stream.
- Start a new conversation without carrying over the previous session.
- Visible terminal errors and recovery after a failed or disconnected request.
- Persistent interactive UI tables with shared human/agent filtering, sorting, column visibility, and paging.
- A saved-table picker for reopening tables independently of the current conversation.

The production project workspace adds CSV/TSV/XLSX import, voice drafts, drawings, images and brain scenarios. Chat uses the shared KitChat component with Markdown content; artifact receipts open editors in the working area. See [IntoCaht workspace](production-workspace.md) for persistence, interactions and current limits. Cell editing and arbitrary file operations are not exposed.

## Interactive tables

Ask the agent to generate a dataset, for example: “Create a sample products table with name, category, price, and stock.” The `create_table` function saves a UI table neuron and returns its stable ID and an authoritative snapshot. Flutter renders the result as an interactive table. It does not parse Markdown into persisted data.

The table stores typed columns (`text`, `number`, `date`, `boolean`), stable row IDs, original rows, AND-combined filter predicates, sorting, visible columns, and a revision. Numeric and boolean cells are native JSON values; dates are ISO `yyyy-MM-dd` strings; null represents a missing value. Supported filter operators are equality/inequality, text contains, comparisons, and null checks, validated against the column type.

Numbers are limited to 15 significant decimal digits and absolute values at most 9,007,199,254,740,991, within the backend decimal scale. Inputs that would lose precision are rejected. This keeps browser JSON, agent tools, and server comparisons consistent. Text cells are limited to 4,000 characters.

Filtering does not remove source rows. Clear filters restores the original dataset. Page position is transient; filters, sort, and column visibility are persisted. The first version accepts up to 1,000 rows, 32 columns, and 64 filters, rejecting larger inputs rather than silently truncating them. Reads default to 50 rows and return at most 200 rows per page with total and filtered counts.

The UI and `update_table_view` tool use the same table service and must provide the revision they read. A stale update produces a conflict and requires a fresh read. Agent instructions require `read_table` before answering table-dependent questions or modifying a view, since UI controls can change it outside the conversation. Additive requests preserve existing predicates. Explicitly attached tables contribute their IDs and view state to outgoing messages. Opening an editor alone does not attach it; the context picker can attach several artifacts.

Available tools are `create_table`, `read_table`, `update_table_view`, and `list_tables`. Successful table tools return `kind: "table"`, an ID, revision, schema, view state, counts, and a bounded row page. Expected failures return `kind: "tableError"` with a code and explanation; they are not successful mutations.

The existing owner authentication gate protects `GET/POST /kit/tables`, `GET /kit/tables/{id}`, and `PUT /kit/tables/{id}/view`. Table data and view state use the neuron persistence configured for the host. With durable storage, they survive restarts and are discoverable without chat history; an explicitly in-memory development host remains volatile. Chat still streams directly through the agent, while table tools invoke data neurons without enabling graph HTTP capabilities or legacy chat turns.

## Configuration

The AppHost already selects `IGpt56Luna` and calls `WithTavilySearch()`. Supply its normal user secrets:

```powershell
dotnet user-secrets set "Parameters:openai-api-key" "YOUR_OPENAI_KEY" --project src/Aspire/DigitalBrain.AppHost
dotnet user-secrets set "Parameters:tavily-api-key" "YOUR_TAVILY_KEY" --project src/Aspire/DigitalBrain.AppHost
```

For a directly configured kernel, use the existing model settings and these search settings:

| Setting | Value |
| --- | --- |
| `DigitalBrain:AI:Default:Model` | `IGpt56Luna` or another configured tool-capable model marker |
| `DigitalBrain:AI:OpenAI:ApiKey` | Model provider key |
| `DigitalBrain:AI:Tavily:Enabled` | `true` |
| `DigitalBrain:AI:Tavily:ApiKey` | Tavily key |

Keys stay on the server. The agent advertises web search only when `IWebSearch` is registered. Missing model configuration fails agent startup; an enabled search service also requires its key. With search disabled, the agent can still converse and is instructed to state that it cannot search.

The existing OpenAI provider factory supplies `reasoning_effort: none` for function-tool calls when the caller has not selected an effort. This matters for GPT-5.6 models on Chat Completions; construct clients through the provider factory rather than bypassing its options.

## Conversation and stream lifecycle

Flutter posts a new user message with an AG-UI `threadId` and `runId`. It retains the returned thread ID and, after the entire successful response closes, uses the completed run ID as the next `parentRunId`. Only new messages are sent; history belongs to the hosted session store.

The session store is **in memory** and intended for this single-owner, single-instance slice. Restarting the kernel loses conversation memory. Flutter retains project conversations and transcripts on the device, but that does not restore lost server model memory. Multi-user or multi-instance deployment requires an isolated durable session store.

The pinned adapter emits `RUN_FINISHED` before saving the session. The Flutter client therefore drains successful responses to EOF before enabling the next send. It aborts only on Stop, errors, disposal, or a new conversation. Server failures produce a sanitized `RUN_ERROR`; provider details are logged server-side.

`BasicAuthGate` continues to protect `/agent` using the existing owner credentials. Basic conversational and web-search tests run without an Orleans silo; table integration tests use the UI module in a test silo. The kernel hosts its existing modules.

## Optional graph

Read-only brain snapshots and event streams are available to the on-demand live brain editor. Mutation, activity, surface and graph-MCP capabilities remain optional; set `DigitalBrain:Graph:Enabled=true` to expose them. Generated scenarios are editable drafts and do not execute integrations. The old `/chats/{name}/send`, `/turns`, `/events`, and voice-send routes are no longer mapped by the production host; internal legacy graph/chat types and their regression tests remain dormant.

## Verification

`ConversationalAgentFacts` exercises the real AG-UI HTTP adapter and MAF function loop with controlled external dependencies: search calls and results, session continuity and separation, streaming before completion, cancellation, authentication, and terminal errors. Flutter protocol and widget tests cover framing, source cards, cancellation, failure recovery, and waiting for EOF before continuing.

`TableAgentFacts` verifies structured table results through the real AG-UI function loop, reading a filter applied through HTTP, agent updates, conflicts, and authentication. `TableNeuronFacts` covers applied-command outcomes, competing writes, nondestructive views, and discovery after a cold file-storage reload. `TablePolicyFacts` checks types, bounds, dates, nulls, and numeric precision. Flutter tests cover shared table cards, saved-table reopening, active-table context, filters, sorting, columns, pagination, conflict recovery, errors, and narrow-screen layout.

A live smoke test with the configured OpenAI model and Tavily returned `TOOL_CALL_START(search_web)`, real source URLs, streamed text with a Microsoft Learn citation, and `RUN_FINISHED`.

A separate live model check created a table, read a filter applied through the HTTP API, and added another filter while retaining the existing one, using `create_table`, `read_table`, and `update_table_view`.
