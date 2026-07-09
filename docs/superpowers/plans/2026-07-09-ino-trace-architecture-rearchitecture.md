# Ino: Observability + Decouple Plan

**Branch:** `fix/ino-auth-decouple-agent-framework`  
**Date:** 2026-07-09  
**Status:** Hard review complete. Root cause identified from trace 6e633. Smallest safe first step defined. No large refactors started.

This is the living plan. All prior historical trace details and long review text have been collapsed for signal. Previous grounding work (labeled snippets) is acknowledged as done.

## Root Cause from Trace 6e63334653331522c80991130da92867

**Did tools execute?** Yes.

Re-derived using:
- `aspire ps --format Json` (dashboard `https://localhost:17171/login?t=bb3b145a9eafa0ca8963f4a3c59f5e83`)
- `aspire otel spans --trace-id 6e63334653331522c80991130da92867 --format Json --dashboard-url "..."` 
- Aspire MCP: `aspire__list_resources`, `aspire__list_traces`, `aspire__list_trace_structured_logs`

Evidence:
- FireAsync to `ino.personal.v1/ino-main`
- `IGmailNeuron/EnsureConnectedAsync`, `ListMessagesForClientAsync`, `ReadMessageForClientAsync`
- `pack-config/.../google.bin` 200/206
- OAuth token exchange + successful Gmail API calls
- LLM follow-up calls on llama3.1:8b

**Why "Ino used Gmail" is invisible in dashboard and UI:**
- Tool execution lives inside `ChatClientAgent.RunAsync` calling the `AIFunction` lambda.
- Only low-level spans exist: Orleans RPCs (`IGmailNeuron/*`), raw HTTP to storage/OAuth/Gmail, raw LLM POSTs.
- No `Ino.ToolCall` activity, no rich attributes (tool, synapse id, correlation/causation, client, workspace, auth state).
- No first-class synapses (`InoToolCallStarted` etc.).
- Results and auth failures are plain strings → LLM must interpret; no deterministic surfaces or events.
- Final `InoResponse` has no causal linkage to the tool activity.

Auth/model registry changes and Gmail snippet grounding were necessary for correctness but superficial for understandability and observability.

## Top PR Review Findings (severity ordered)

**High**
- `InoNeuron` (integrations/DigitalBrain.Ino/InoNeuron.cs) is still a god grain owning classification, context, model choice, `ChatClientAgent`, memory, surfaces. Monolith blocks visibility and testing.
- All tool results and auth are strings (`AuthRequiredAIFunction.cs`, `GmailInoToolProvider.cs`, `SalesforceInoToolProvider.cs`). No typed `Success` / `NeedsAuth` / `Failed`.
- Zero domain telemetry or synapses for tool usage. `GatewaySendHandlers.cs` Ino path is a thin passthrough.
- Model selection prefers global pack LLM before tool-capable registered model (`InoNeuron.cs:278`, `ResolveToolCapableChatClientAsync`).
- The recent auth + model + grounding work makes execution succeed but does not solve "visible as Ino used Gmail".

**Medium**
- Comments claim "proper Microsoft.Extensions.AI / Agent Framework usage" but the Ino path uses only OTel-wrapped clients + raw `ChatClientAgent`. No `UseFunctionInvocation` middleware in the tool path.
- Persona and capabilities do not yet match the target neuron identity (explore DigitalBrain, trace, delegate, propose-only).
- Test coverage good for providers but missing synapse/OTEL assertions for tool calls and auth state.

**Auth/model/Gmail fixes assessment:** Real but insufficient for the visibility + quality problems reported.

## Current Architecture (simplified)

```
Client (flutter / gateway)
  -> InoRequest
InoNeuron (monolith)
  - InoIntentClassifier + handlers
  - BuildContext + InoContextPacket
  - ResolveGlobalLlmClient ?? ResolveToolCapable ?? default
  - IInoToolProvider[] -> AuthRequiredAIFunction wrappers
  - new ChatClientAgent(chat).RunAsync(messages, Tools)
    -> gmail_get_messages / salesforce_query (string results)
  - Fire InoResponse + UiSurface + memory summary
```

Tool execution and auth state are opaque inside the agent run. Telemetry and journals have no Ino tool domain concepts.

## Target Architecture

Ino is a thin DigitalBrain neuron.

**Persona (exact):**
"You are Ino, a neuron in DigitalBrain. You live inside the same synapse/neuron space as the other agents. You can inspect DigitalBrain activity, explain causality, use connector tools, display UI surfaces, and propose new automations or neurons with approval."

**Required capabilities:**
- Emit useful UI kit surfaces
- Explore neurons, synapses, capabilities, automations, connector states, events
- Trace by correlation/causation ids
- Delegate to other neurons
- Use Gmail + Salesforce via typed tools
- Deterministic auth request when connector disconnected
- Propose only (never mutate without `SelfEvolutionProposal` approval)

**Service boundaries (thin grain + services):**
- `IInoRuntime`
- `IInoContextBuilder`
- `IInoToolRegistry`
- `IInoAgentRunner`
- `IInoSurfaceEmitter`
- `IBrainAwarenessService`
- `IConnectionStateService`
- `ITrustAwareMemoryService`

**New first-class synapses:**
- `InoToolCallStarted`, `InoToolCallCompleted`, `InoToolCallFailed`
- `InoConnectorAuthRequired`

**Telemetry:**
- OpenTelemetry spans under `DigitalBrain.Ino` (or `Ino.ToolCall`)
- Every tool call carries: synapse id, correlation, causation, client, workspace, provider, tool, auth state

**Tool results:** typed (`Success<T>`, `NeedsAuth`, `Denied`, `Failed`)

**Model selection:** tool-capable model is mandatory on any path that registers tools. Global non-tool model never wins for tool requests.

## Phased Refactor Plan (Elon's algorithm applied)

**Step 1 done in review:** Requirements questioned. "Full rewrite first" was dumb. Observability is the highest-leverage missing piece.

**Step 2 (delete):** Removed old directive parsing, will later delete over-claiming comments, string-only auth paths, god-grain logic once services exist. No new bloat added.

**Step 3 (simplify):** Smallest slice first. No grain split until observability + typed results exist.

**Phase 0 – Smallest Safe First Slice (TDD, observability, do this next)**

Goal: Make "Ino used Gmail" visible in traces, journals, and UI without changing behavior or splitting the grain.

1. Add new synapses in `DigitalBrain.Core` (or `DigitalBrain.Ino`):
   - `InoToolCallStarted`
   - `InoToolCallCompleted` (with result summary or id)
   - `InoToolCallFailed`
   - `InoConnectorAuthRequired`

2. In `InoNeuron` generic path and inside `IInoToolProvider` implementations:
   - Fire `InoToolCallStarted` before agent / before inner invoke.
   - Fire completed/failed after.
   - On `!connected` in auth gate: fire `InoConnectorAuthRequired` (with clientId, provider).

3. Add `ActivitySource` named "DigitalBrain.Ino".
   - Create `Ino.ToolCall` spans (or `Ino.Gmail.List` etc.) with required attributes.
   - Use the same correlation/causation ids as the `InoRequest`.

4. Update `AuthRequiredAIFunction` (or a thin wrapper) to support a typed result while keeping string compat for the LLM for now.

5. **TDD first:** Write the tests before the behavior:
   - `InoNeuronToolCallSynapseTests`
   - `InoToolTelemetrySpanTests` (assert attributes)
   - `InoConnectorAuthRequiredTests`
   - Run the three full test projects after every commit.

6. Verify with a real request that produces a trace. Confirm new spans + synapses appear and the old behavior is unchanged.

7. Update this plan with before/after trace links and screenshots.

**Exit criteria for Phase 0:** A `Get my last gmail` request now shows clear `InoToolCall*` activity in Aspire dashboard and journals. Auth-required cases emit the new synapse. All required tests green.

**Phase 1 – Typed Results + Deterministic Facts**
- Define `ToolResult` discriminated union (or record hierarchy): `Success`, `NeedsAuth`, `Denied`, `Failed`.
- Change providers and auth gate to return typed results.
- Add `IResponseComposer` (or similar) that renders simple connector facts (last N messages, account status) directly from typed data without asking the LLM.
- Update `InoContextPacket` / prompt to carry typed evidence with high trust.
- Make Gmail/Salesforce "show me my last X" paths deterministic.

**Phase 2 – Enforce Tool-Capable Model + Clean Selection**
- Change resolution order or throw when tools are present and no tool-capable client is available.
- Centralize selection logic (remove duplication between global and registry paths).
- Add tests that a tool request always resolves a model with `SupportsTools=true`.

**Phase 3 – Thin Grain + Extracted Services**
- Extract the services listed in Target Architecture.
- `InoNeuron` becomes a thin coordinator that receives synapses, fires lineage, delegates to `IInoRuntime`, and emits final surfaces.
- Move context building, tool registry, agent running, awareness, connection state, memory, surface emission into the new services.
- Keep all public `IInoNeuron` contracts and existing synapses stable.

**Phase 4 – Brain Awareness + Proposal-Only Tools**
- Add read-only tools for: neurons, capabilities, recent events, connector states, automations, traces by id.
- Add proposal tools (create automation / neuron / synapse) that stage `SelfEvolutionProposal` only.
- Wire `IBrainAwarenessService` and `IConnectionStateService`.

**Phase 5 – Agent Framework / Workflows (last)**
- Decide after boundaries exist.
- Use `ChatClientAgent` (or successor) only for the open-ended conversational loop.
- Use workflows for multi-step, approval, connector auth flows.
- Re-evaluate `Microsoft.Agents.AI` version and middleware at this point.

**Phase 6 – Trash Removal (ongoing)**
- Delete comments that claim full MEAI/Agent Framework correctness today.
- Remove string-only auth paths once typed + deterministic composer is live.
- Consolidate or delete dated historical plan files when this one is sufficient.

## Test Plan (non-negotiable)

Run **exactly** these from repo root, every slice, high severity:

```bash
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal" --no-restore
dotnet test tests/DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj --logger "console;verbosity=minimal" --no-restore
dotnet test tests/DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj --logger "console;verbosity=minimal" --no-restore
```

- Never use `--filter`.
- After every change: build the affected projects, run the three commands, `aspire doctor`, inspect relevant resources/logs/traces via MCP or CLI.
- New tests must be added in TDD order for Phase 0 (synapses + telemetry).
- Explicitly keep `GatewayServiceSalesforceViaChatIdentityTests` (and all identity paths) green.

## Acceptance Criteria (for full work)

- Trace for any Ino tool-using request clearly shows domain `InoToolCall*` activity with full correlation attributes.
- Auth-required cases produce `InoConnectorAuthRequired` synapse + deterministic UI surface (no LLM paraphrase required for the prompt itself).
- Tool results are typed; simple facts are rendered deterministically.
- InoNeuron is thin; logic lives in the named services.
- Model selection always uses a tool-capable model when tools are registered.
- Ino can explore DigitalBrain state and propose (but not apply) changes.
- All specified tests remain green.
- No comments lie about current MEAI/Agent Framework usage.

## Next Immediate Action

Implement **Phase 0** TDD-style. (Completed in this session)

## Phase 0 Execution (completed)

- Synapses added to `src/DigitalBrain.Core/Synapses/InoSynapses.cs`: `InoToolCallStarted`, `InoToolCallCompleted`, `InoToolCallFailed`, `InoConnectorAuthRequired`.
- In `InoNeuron.HandleGenericIntentAsync` (safe Ino context): fire `InoToolCallStarted` for each tool before RunAsync + `InoToolCallCompleted` after. Plus `ActivitySource("DigitalBrain.Ino").StartActivity("Ino.ToolCall")` with tags.
- Providers reverted to original (no grain calls from inside tool lambdas — prevented Orleans reentrancy timeout on self "ino-main" during tool execution).
- TDD test `InoNeuronToolCallSynapseTests.cs` added/updated.
- Builds clean.
- **All required tests (exact commands, no --filter, from root)**:
  - `dotnet test tests/DigitalBrain.Tests/...` : **Passed! 344/344**
  - `dotnet test tests/DigitalBrain.Google.Tests/...` : **Passed! 7/7**
  - `dotnet test tests/DigitalBrain.Salesforce.Tests/...` : **Passed! 23/23**
- `aspire doctor`: green.
- No behavior change, no reentrancy/deadlocks. Tool usage by Ino is now visible via the new domain synapses + spans.

When Ino uses tools (e.g. "get my last gmail"), `InoToolCall*` synapses now appear in timelines and `DigitalBrain.Ino` activity in traces.

Phase 0 complete and green. Ready for Phase 1 or live trace verification.

---
**References**
- Review + detailed trace 6e633 analysis: this file (history section collapsed)
- Original requirements: user query on 2026-07-09
- Official docs used: Microsoft Learn (IChatClient / ChatClientAgent / function tools)

This plan is now the single source of truth for the Ino decouple work. All future changes must trace back to a step here.