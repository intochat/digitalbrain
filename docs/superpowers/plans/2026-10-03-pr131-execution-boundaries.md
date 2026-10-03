# PR 131 execution boundaries

User approved the combined in-chat design and requested implementation on 2026-10-03.

## Tasks

1. Connect receiving-side neuron authorization to existing enforcement. Module-owned target policy must distinguish brain-owned targets, explicitly public operations, creation and trusted internal calls. Deny foreign and unclassified external targets without deriving ownership from discovery history. Exercise real grain dispatch.
2. Share runtime/MCP cookie identity configuration and key storage through host composition. Test a runtime-issued cookie on a separate MCP host, anonymous 401 and membership 403.
3. Separate bounded capability discovery from explicit selection. Selection resolves current resources and offers only selected tools through existing factories/sessions. Preserve typo recovery, partial errors, resource ownership checks and bounded enumeration.
4. Record every attempted tool call with one terminal result. Preserve fatal/cancellation semantics, expose failure code/reason in durable receipts and Flutter, and test unavailable/retry/budget/error behavior.

## Verification

Run relevant per-project .NET unit and E2E suites, Flutter analysis/tests for receipt changes, and Aspire health smoke. Do not build the solution. Test new failure cases before production changes. Review the combined diff before completion.

## Execution ledger

- Baseline: clean PR branch codex/app-scenarios-identity at e71b14df0.
- Ruling: continue in the current clean PR checkout to preserve the user's existing PR workflow; isolate build output if live processes lock assemblies.
- Interface dependency: discovery selections constrain presentation, while receiving-side module policies remain the invocation authority. A discovery ID never grants permission.
- Interface dependency: unified terminal tool results flow through AgentNeuron, Assistant usage/receipt projection and Flutter; preserve serializer IDs and append new fields.
- Target-boundary regression was reproduced with a real foreign IApp.Read before the filter change. Canonical workspace keys share one strict parser; aliases use module-owned persisted state. Unclassified external targets fail closed. Internal module provenance travels without replacing the caller. Platform exemptions follow excluded contracts, never implementation assemblies.
- Compatibility ruling: provision real brain membership in stamped test fixtures. Script files bind their initial writer and retain stored AppBrain/OwnerContext; existing bound-behavior guards remain authoritative. Scratch scenario helpers can use the host's supplied BrainScope.
- Cookie regression reproduced as HTTP 401 across separate hosts; runtime registration cookie now authenticates MCP, foreign route returns 403, foreign raw app key fails, public package read succeeds. Shared Aspire identity settings and key-store reference are covered.
- Discovery is module metadata -> bounded provider browse -> exact selection. App selection pins package revision, opens one resource and rechecks atomically at IApp.Invoke. Legacy index migration advances one persisted batch per browse and reports retry rather than silently omitting installs.
- Tool regression reproduced with zero starts for unavailable attempts. Every consumed attempt now records a terminal success/error; app failures normalize to the same envelope, cancellation closes durable pending agent events, and result serialization is inside the tool failure boundary. Flutter displays code and reason.
- Fresh review found four gaps (app failure envelope, cancellation terminal events, legacy index migration, result serialization); all were fixed with regression coverage. Follow-up found an overbroad platform implementation exemption. A public IConnectionRequests cross-brain test reproduced it RED; contract-based exemption made it GREEN and Platform 121/121 still passes.
- Verification so far: AI 90, Apps 103, Assistant 63, CSharp 77, Postgres 74 (3 live-config skips), Registry 15 (1 live-Qdrant skip), Kernel 73, Platform 121, Aspire hosting 15, IntoChat 9, MCP 8 tests pass. Flutter targeted analysis clean, receipt panel 8/8. IntoChat real AppHost health E2E passes. Broader receipt E2E exposed the old scripted model still assuming discovery activates tools; fixture updated to discover -> select -> invoke and rerun pending.
- Final verification: Apps 104/104 including the public-platform-contract security regression; Platform 121/121; Flutter module 83/83, shell analysis clean and receipt panel 8/8. Real E2E passed: IntoChat startup health, Assistant durable receipt journey, Apps document ownership over HTTP, CSharp authoring composition, and both OpenAI/OpenRouter provider adapters. Four optional live database/vector-store unit tests remain skipped for missing configuration.
- Tasks 1–4 complete. Review fixes are included. Existing PR branch was fetched and matched the starting head before integration; no review replies or thread-resolution messages were posted.
