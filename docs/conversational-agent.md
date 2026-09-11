# Conversational agent

The default Flutter shell talks directly to a single Microsoft Agent Framework agent through `POST /agent`. The endpoint uses `AddAGUIServer` / `MapAGUIServer` from `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` 1.20.0-preview.260831.1. Chat does not schedule neuron signals or read turn journals.

## Available now

- Streamed Markdown responses, including clickable citations.
- Server-owned conversation history between messages.
- The `search_web` function backed by Tavily, with visible progress and source cards.
- Stop a response by cancelling its HTTP stream.
- Start a new conversation without carrying over the previous session.
- Visible terminal errors and recovery after a failed or disconnected request.

This first slice does not expose CSV/XLSX upload, table filtering, graph editing, voice input, or arbitrary file operations. The existing Flutter component kit is retained for the next structured-data tools. Structured search results already render as source cards; other tool results have a readable JSON fallback.

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

The session store is **in memory** and intended for this single-owner, single-instance slice. Restarting the kernel loses conversation memory. Reloading Flutter starts a new conversation; there is no saved-history browser yet. Multi-user or multi-instance deployment requires an isolated durable session store.

The pinned adapter emits `RUN_FINISHED` before saving the session. The Flutter client therefore drains successful responses to EOF before enabling the next send. It aborts only on Stop, errors, disposal, or a new conversation. Server failures produce a sanitized `RUN_ERROR`; provider details are logged server-side.

`BasicAuthGate` continues to protect `/agent` using the existing owner credentials. The agent tests run without an Orleans silo, proving chat has no graph execution dependency. The kernel still hosts its existing modules.

## Optional graph

Graph, activity, surface, and graph-MCP HTTP capabilities are disabled by default. Set `DigitalBrain:Graph:Enabled=true` on the kernel to expose them. The new Flutter shell does not subscribe to their streams. The old `/chats/{name}/send`, `/turns`, `/events`, and voice-send routes are no longer mapped by the production host; internal legacy graph/chat types and their regression tests remain dormant.

## Verification

`ConversationalAgentFacts` exercises the real AG-UI HTTP adapter and MAF function loop with controlled external dependencies: search calls and results, session continuity and separation, streaming before completion, cancellation, authentication, and terminal errors. Flutter protocol and widget tests cover framing, source cards, cancellation, failure recovery, and waiting for EOF before continuing.

A live smoke test with the configured OpenAI model and Tavily returned `TOOL_CALL_START(search_web)`, real source URLs, streamed text with a Microsoft Learn citation, and `RUN_FINISHED`.
