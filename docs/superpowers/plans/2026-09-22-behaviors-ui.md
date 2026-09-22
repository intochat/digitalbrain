# Behaviors UI Implementation Plan

> Execute inline using the executing-plans workflow. Approval was supplied for the design and implementation sequence in the conversation.

**Goal:** Manage assistant-authored behaviors from Applications.
**Architecture:** Durable application catalog plus the existing scoped execution tools; Flutter app window with list/detail and existing chat integration.
**Tech stack:** Existing .NET, Orleans, Flutter, HTTP client and Material components; no new dependencies.
**Spec:** `docs/superpowers/specs/2026-09-22-behaviors-ui-design.md`

## Global constraints

- Preserve current uncommitted authoring fixes; work in this checkout.
- Scope every catalog read/mutation to the authenticated workspace.
- Backend state is authoritative; failed refresh disables mutations.
- Deploy only the exact current checked artifact; no optimistic Ready.
- Declared connections are documentation, never evidence of actual execution.

## Review focus

1. Switching workspace or behavior during a request must not apply stale responses.
2. Failed checks and later edits must invalidate UI deployment eligibility.
3. Repeated chat saves must not duplicate catalog entries or overwrite names.
4. A connection failure must not leave Start/Stop/Deploy enabled.
5. Opening edit/repair must not discard an existing composer draft.

## Tasks

- [ ] Catalog: add `BehaviorCatalogStore.cs` and tests for durable workspace isolation, idempotent registration, metadata revision conflict, and checked-source retention. Use `DurableDocumentStore<T>`; resolve root from `WorkspaceStorageOptions`. Integrate registration into scoped draft saves and authoring.
- [ ] Projections and routes: add `BehaviorManagement.cs`, list/detail/describe endpoints, and `behavior_describe` tool. Return metadata, current draft/check/program, policy and bounded logs. Preserve revision-based lifecycle routes. Test detail deploy eligibility and current-check identity.
- [ ] Client and app window: add `behaviorRequest` to `DigitalBrainUiClient`; add `behaviors/behavior_manager.dart` and focused detail widgets. Activate launcher and local app identity. Add widget tests for filters, status, deployment gating, disconnect and narrow navigation.
- [ ] Assistant: create focused conversations for create/edit/repair; add Open behavior to behavior tool results without treating metadata as executable instructions. Keep existing drafts intact. Test launcher and conversation context.
- [ ] Verify: run IntoChat unit suite, Flutter analyze and focused/full shell tests, build local app, exercise native create/list/detail/check/lifecycle flow, run behavior E2E and a final independent review. Record concrete results below.

## Execution record

Implemented catalog, workspace projections, list/detail UI, assistant links, validation and lifecycle controls. Final review identified configuration-only deployment, old log pages and stale post-command refreshes; all three were corrected with focused regression tests.

Verification: IntoChat unit tests 29 passed; behavior UI widget tests 5 passed; Flutter analyze clean; Windows build succeeded. Behavior runtime E2E passed. Full shell testing exposed an existing table-window pumpAndSettle timeout, reproduced separately against the committed baseline. Native verification reached the rebuilt application but was stopped by the user with Escape before exercising the manager.

Subsequent direct MCP verification found and fixed the tool target factory registration. Two validated demo drafts were created in the user's workspace; source, metadata and passing check results survived a full Aspire restart. See `2026-09-22-behavior-mcp-persistence-verification.md`.

Remaining verification: complete native manager lifecycle walkthrough and add dedicated launcher/conversation-context coverage. The user requested a commit of the current work on 2026-09-22.
