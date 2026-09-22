# AI Neurons Implementation Plan

**Goal:** implement callable models and durable conversational agents for generated C# behaviors.

**Architecture:** provider adapters sit behind serializable inference contracts; agents own history and execution policy. Public agent methods follow the IAW reference. Existing Ask and model markers remain supported.

**Spec:** ../specs/2026-09-22-ai-neurons-design.md

## Constraints and decisions

- Work in the user workspace; it was clean at start. Preserve current consumer compatibility.
- User authorized implementation directly; record the plan and proceed without another approval cycle.
- Stage 2 owns scheduling, workspace logic, callbacks and behavior compilation.
- Use existing net11.0 dependencies and Orleans serialization aliases.
- Parallel work is limited to independent model/media areas; agent integration stays local.

## Tasks

- [x] Models: serializable inference messages/options/descriptors, generic and 16 typed LLM neurons; strict provider identity/settings validation, profiles and lifecycle signals. Provider factories and typed neurons grouped in AI/Providers/{provider}.
- [x] Media: separate image, embedding, recognition and synthesis contracts/adapters with capabilities and tests; real OpenAI speech transport mapping verified locally.
- [x] Agents: persisted configuration/history/run state, rich results, streaming, cancellation, metadata, usage and compatible Ask. History/reactivation/isolation/cancellation/abandonment/recovery tests.
- [x] Tools: native/MCP selection through runtime adapters; bounded invocation loop, structured tool history, explicit call IDs and resource cleanup. Model selection/tool-start acknowledged before external work.
- [x] Integration: dedicated AI E2E project, solution entry, usage documentation and independent review. Verification recorded below.

## Review focus

Abandoned streaming enumeration must stop execution. Cancellation must interleave with provider waits. Persisted active runs must recover as interrupted. Provider settings must not disappear between tool/non-tool paths. Unsupported media/options must fail before external calls.

## Execution ledger

- Planning: inspected IAW IAgent and current AI/Flutter contracts using CodeGraph. Context7 quota exhausted; Orleans cancellation/streaming documentation checked on Microsoft Learn instead.
- Regression: initial second Ask returned only the second message; the history test failed before the durable implementation and passed afterward.
- Ruling: adopt IAW conversational actor identity, retaining run records inside the agent rather than requiring callers to manage separate run/conversation neurons. Scheduling, workspace and UI callbacks remain stage 2.
- Ruling: use a JSON envelope with explicit content discriminators for agent storage. Orleans wire serialization already passed, but the existing JSON-backed storage failed reconstructing abstract content after reactivation. Reactivation test now passes.
- Ruling: inline media is bounded to 8 MiB until a shared artifact store exists. Media uses configured default transports; realtime voice and per-request media model routing remain unimplemented and documented.
- Review: independent reviewer identified reasoning-validation bypass and unbounded data URI decoding. Both reproduced with failing tests and fixed. Ollama reasoning overwrite reproduced through the real SDK against a local HTTP fixture and fixed; typed context-window settings also verified.
- Execution review: failure events cannot disappear behind a full output buffer; restoring the old nonblocking terminal write made the regression test fail. Tool-start must be recorded before invocation; old code failed the new test, acknowledgment now gates execution. Postcommit agent notifications are best effort and cannot undo committed success.
- Verification: full solution built with zero warnings/errors; IntoChat unit 8/8; DigitalBrain runtime unit 47/47; separate-process AI E2E 1/1. Final AI unit 42/42 after formatting; final solution build 0 warnings/errors; final AI E2E 1/1; git diff --check clean.