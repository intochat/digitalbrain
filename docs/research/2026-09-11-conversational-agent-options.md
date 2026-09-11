# Conversational agent options

Research checked 2026-09-11 against Context7, Microsoft Learn, Microsoft developer blogs, official source, and NuGet metadata. Proposal only; no implementation changes.

## Recommendation

Keep Flutter and Microsoft Agent Framework. Replace the current custom turn orchestration with one conversational agent, a session per conversation, streaming output, and ordinary function tools. Prefer AG-UI for the client protocol if accepting its preview hosting package is reasonable; otherwise keep the same agent behind a small application-owned SSE endpoint. Render validated tool results with existing Flutter widgets. A general generated-layout engine is unnecessary for the first spreadsheet/table/chart experience.

This is an architectural recommendation, not a Microsoft-prescribed application design. AG-UI supplies events; Flutter still supplies the actual components and interaction behavior. The current .NET integration explicitly covers streaming, backend and frontend tools, approvals, state snapshots/deltas, and hosted session continuation. [Microsoft .NET integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/ag-ui/).

## Current names and release status

- Agent Framework core reached 1.0 GA on April 3, 2026. Latest .NET release visible on the official releases page when checked is **1.20.0, August 31, 2026**, matching this repository's pin. Core GA does not imply every integration package is GA. [GA announcement](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/), [1.20.0 release](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.20.0).
- The current hosting extension is **`MapAGUIServer`**, with **`AddAGUIServer`** registration. Older examples use `MapAGUI` and `AddAGUI`; Microsoft's migration README documents the rename. The remembered generic `MapAgent` is not the endpoint to target in these current samples. [Migration README](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.AGUI/README.md).
- Exact hosting package: `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`; latest package checked is **`1.20.0-preview.260831.1`**. This was checked directly against the official NuGet flat-container API. Pin it explicitly and validate the Flutter interoperability slice. [NuGet metadata](https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai.hosting.agui.aspnetcore/index.json).
- Tagged 1.20.0 source confirms `app.MapAGUIServer("/agent", agent)` and named-agent `app.MapAGUIServer("assistant", "/agent")`. It maps HTTP POST to SSE and calls `RunStreamingAsync`. [Tagged implementation](https://github.com/microsoft/agent-framework/blob/dotnet-1.20.0/dotnet/src/Microsoft.Agents.AI.Hosting.AGUI.AspNetCore/AGUIEndpointRouteBuilderExtensions.cs).
- Alternative interoperability endpoints are `MapOpenAIResponses` and `MapOpenAIChatCompletions`, with `AddOpenAIResponses` and `AddOpenAIChatCompletions`. These are useful when clients need OpenAI-compatible APIs; that compatibility alone does not define a Flutter table/chart UI contract. [Microsoft endpoint documentation](https://learn.microsoft.com/en-us/agent-framework/hosting/self-hosting/openai-endpoints).

## Options

| Option | What it buys | What the application owns | Assessment |
| --- | --- | --- | --- |
| Agent Framework + AG-UI + Flutter widgets | Standard text, tool and state events; agent sessions and function loop | Flutter event adapter, typed widget rendering, data tools, session storage | Preferred if preview integration is acceptable |
| Agent Framework + small custom SSE API | Retains agent/session/tool abstraction; controls a small explicit UI contract | Event schema, SSE mapping, errors, cancellation and reconnect behavior | Good alternative for minimal protocol surface |
| Microsoft.Extensions.AI + custom SSE | Small provider-neutral model/tool layer | Conversation history, application agent lifecycle and transport | Viable, but removing Agent Framework gains little here |
| Agent Framework + OpenAI-compatible endpoint | Works with compatible clients and tooling | UI artifact/state mapping and Flutter integration | Useful secondary endpoint rather than the primary UI choice |

Microsoft.Extensions.AI is the lower layer, not a competing required replacement: `IChatClient.GetStreamingResponseAsync`, `ChatClientBuilder.UseFunctionInvocation`, and `AIFunctionFactory.Create` provide streaming and local tools without Agent Framework. The application then manages the surrounding conversation and HTTP behavior. [Official .NET Extensions source examples](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/README.md). The foundational packages became GA in May 2025. [Microsoft announcement](https://devblogs.microsoft.com/dotnet/ai-vector-data-dotnet-extensions-ga/).

## Best starting examples

1. [Microsoft AGUIClientServer end-to-end example](https://github.com/microsoft/agent-framework/tree/dotnet-1.20.0/dotnet/samples/05-end-to-end/AGUIClientServer): directly relevant to hosted streaming chat and web search. Its README uses an OpenAI Responses client, `HostedWebSearchTool`, an in-memory session store, and `MapAGUIServer`.
2. [Current .NET getting-started guide](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/getting-started): minimal registration and endpoint, plus conversation continuation.
3. [Microsoft .NET AG-UI scenarios](https://github.com/microsoft/agent-framework/tree/dotnet-1.20.0/dotnet/samples/02-agents/AGUI): focused samples; preferable to translating the Python Dojo examples blindly.
4. [Backend tool rendering](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/ag-ui/backend-tool-rendering): function tools execute on the server and clients render their event results.

The 2025 [.NET 10 announcement](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/) includes the older `MapAGUI` spelling. Use it for architectural context, and current tagged source for exact syntax.

## Flutter and structured data

The AG-UI repository lists Dart as a **community** SDK; the published `ag_ui` package supports SSE, tool events and state updates, with 0.3.0 current when checked. It is a protocol client, not a Microsoft-supported Flutter widget suite. [Protocol repository](https://github.com/ag-ui-protocol/ag-ui), [Dart package](https://pub.dev/packages/ag_ui).

Suggested first data contract: a server tool returns a bounded, typed result containing `datasetId`, `viewId`, `revision`, column definitions, sample/page rows, total row count and filter description. Flutter maps that result to a known table widget. Uploading CSV/XLSX uses a deterministic server parser and data store; the model receives schema and small samples rather than the entire workbook. A later `filter_dataset` tool accepts a validated filter expression and returns a new view of the same dataset. User controls can call the same data service directly. These are proposed application choices, not out-of-the-box framework capabilities.

Keep raw datasets and canonical query state on the server. Render declarative results from an allowlisted widget catalogue; avoid asking the model to produce Dart code. Add chart/image components after table/filter behavior works. This supports the user's desired generative UI without first adopting a broad generated-layout framework.

## Session and web-search details

Removing custom turns does not remove conversational history. Keep a stable conversation/session ID and a separate per-request run ID. The AG-UI hosted adapter uses `threadId` for persisted sessions, but without a session store requests are ephemeral. Decide on one history owner so the UI does not resend full history into a server session that already stores it. For multiple users, isolate session lookup by the authenticated principal. [Tagged adapter source](https://github.com/microsoft/agent-framework/blob/dotnet-1.20.0/dotnet/src/Microsoft.Agents.AI.Hosting.AGUI.AspNetCore/AGUIEndpointRouteBuilderExtensions.cs), [continuation guide](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/getting-started).

`HostedWebSearchTool` depends on the model provider; it is not universal web-search capability on arbitrary OpenAI-compatible chat endpoints. The 1.20.0 release specifically corrected the AG-UI example to use Responses for hosted search. Reusing the repository's Tavily-backed `search_web` function is the simplest first path and retains its current provider independence. [Web search documentation](https://learn.microsoft.com/en-us/agent-framework/agents/tools/web-search), [release notes](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.20.0).

## Repository context

`Directory.Packages.props` already pins Agent Framework 1.20.0 and Extensions.AI 10.9.0. The coordinating repository inspection identified `AgentNeuron` calling `RunAsync`, sessions keyed by delivery correlation IDs, and the Flutter UI generating a new turn correlation per message. `ChatEndpoints` provides accepted/completed/failed journal projections rather than direct token streaming. Existing Tavily `IWebSearch` and `WebSearchFunction` implementations, plus Flutter spreadsheet/chart/image components, are candidates for reuse.

Proposed sequence: first bypass graph/turn orchestration for chat, keep graph disabled as an optional separate capability, establish one agent plus stable sessions and streamed text/search results, then add upload/table rendering and filtering. Once the replacement is verified, remove the obsolete chat-turn path and its public contracts rather than wrapping it in another abstraction. Verify continuity across two user messages, streamed text before completion, search tool events/citations, cancellation, and a table filter updating the intended dataset view.
