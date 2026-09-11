# Conversational Agent Implementation Plan

> Execute with the subagent-driven-development skill; backend and Flutter have separate ownership and meet at the AG-UI protocol.

**Goal:** Replace the default graph-driven chat with a streaming conversational agent that can invoke web search.

**Architecture:** ASP.NET Core hosts one Microsoft Agent Framework agent at POST /agent using AG-UI. Flutter sends messages and consumes standard text/tool/run events directly. Conversation state is owned by the hosted session store; the client retains threadId and parentRunId. Existing graph modules remain available separately, not on the default chat path.

**Tech Stack:** .NET 11, Microsoft.Agents.AI 1.20.0, AG-UI hosting 1.20.0-preview.260831.1, Flutter, existing Tavily search integration.

**Spec:** docs/research/2026-09-11-conversational-agent-options.md and the approved proposal in this task.

## Global constraints

- Preserve unrelated uncommitted work; no commits or resets.
- Reuse model configuration and server-side Tavily credentials.
- Flutter receives text deltas and structured search results; it never holds provider keys.
- Chat must operate without graph signals, turn journals, or Orleans scheduling.
- No pretend search results: prove a function call reaches the search service and returns results to the model.

## Task 1: Agent host and search

- [x] Add an HTTP integration test that posts AG-UI input and asserts text events, backend search execution/results, and conversation continuity.
- [x] Run the test and record the missing-endpoint failure.
- [x] Register the agent with the existing IChatClient and search_web AIFunction; map /agent with the official hosting adapter and configure session storage.
- [x] Keep credentials/session ownership bounded to the existing single-owner deployment.
- [x] Verify streamed deltas precede completion, cancellation propagates, and a second request sees previous messages.

## Task 2: Flutter chat

- [x] Add protocol tests for split SSE frames, text/tool/run events, errors, and cancellation.
- [x] Implement authenticated POST /agent through the existing HTTP client.
- [x] Replace default shell chat with a direct conversational screen, streamed text, tool status/results, new conversation, and Stop.
- [x] Retain typed UI kit components for structured results; render search sources as usable links.
- [x] Test the screen with a controlled stream and verify no legacy graph/chat subscriptions start.

## Task 3: Integration and verification

- [x] Retire old default chat HTTP routing and its startup dependencies; keep graph modules optional.
- [x] Document configuration, session lifetime, and supported first-slice behavior.
- [x] Run .NET tests/build and Flutter tests/analyzer; fix regressions attributable to this change.
- [x] Review the integrated diff and report verification plus any external credential limitations.

## Progress

Plan inspected: backend and Flutter share only /agent and the standard AG-UI payload. Existing dirty files are intentional starting state. Backend is owned by the primary agent; Flutter task is delegated independently.


Verified: solution build 0 warnings/errors; full .NET suite 168 passed; focused conversational tests 5 passed; Flutter core/shell tests 38 passed; Flutter analyzer clean; Flutter web build succeeded. A live production-provider/Tavily smoke test emitted search_web, source URLs, a cited answer, and RUN_FINISHED. Review found and fixed completion-before-session-save cancellation; final review clean. Browser visual inspection was attempted but blocked by the in-app browser policy. Legacy graph/chat internal types remain dormant; production chat/voice-turn routes are unmapped. No commits made.
