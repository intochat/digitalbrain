# Compute usage panel implementation plan

**Goal:** Move passive usage receipts from chat into the bottom Compute control, with durable, scoped history and resource details.

**Architecture:** Existing usage/ledger records remain authoritative. A workspace-scoped paged projection supplies recent activity. Reuse the collection UI primitive with neutral usage presentation; show it in an anchored desktop panel or mobile sheet.

**Spec:** `docs/research/2026-09-28-compute-usage-panel.md`, approved by the user's “do it”.

**Constraints:** Preserve chat drafts and approvals. Never mix preview, charged and reserved amounts. Missing usage is unreported. Authorize account/workspace queries. Keep existing file collection behavior. No new dependencies unless necessary. Work in the existing feature checkout; no unrelated changes.

## Tasks

- [x] Backend: add durable activity projection and authenticated cursor paging, record completed agent receipts with reported model usage and tool/data details, expose account summary separately. Test scope, paging/deduplication, persistence and amount semantics.
- [x] Collection: extract reusable `UiCollectionView` from the collection renderer, preserve files, add neutral secondary/trailing usage rows, optional selection, empty/error and paging. Test both modes.
- [x] Shell: typed API models/client, Compute panel and label action, details and pagination, authoritative refresh on late receipt, local historical receipt compatibility, hide passive receipts while retaining approvals. Test desktop/narrow layouts, failure/retry, reload, late receipts and filtering.
- [x] Verify: focused tests then full shell suite, .NET Release tests (running Debug host locks assemblies), static analysis, web build and review.

## Shared API

`GET /workspaces/{workspaceId}/compute/usage?limit=20&cursor=...` returns `{items, nextCursor}`. Item: `{id, occurredAt, title, outcome, previewCompute, chargedCompute, reservedCompute, priceBookVersion, modelUsage: [{provider,model,inputTokens,cachedInputTokens,reasoningTokens,outputTokens,totalTokens,usageReported}], calls: [{appId,operation,succeeded}], touched: [{source,rowsRead,readOnly}]}`. Amount fields nullable when unknown. `GET /compute/summary` returns `{chargedCompute,reservedCompute,limitCompute,spentCompute,hardStopped}` with account scope. API differences must be communicated before integration.

## Review focus

Cross-workspace disclosure; identical timestamps/cursor stability; late or duplicate receipts; estimates mistaken for settled charges; missing/legacy usage and small-screen overflow.

## Progress

Plan authorized by existing proposal approval. Backend and reusable UI are independent file scopes; shell integration follows their declared contracts. Research note is carried forward unchanged.

Ruling: Wallet charges and settled app allowances lack a reliable correlation key. Return `chargedCompute` (wallet), `settledCompute` (app allowances), and `reservedCompute` separately on items and summary; never add wallet/app values into a guessed total.

Collection implementation: extracted UiCollectionView with backward-compatible contract defaults. Nine focused widget tests pass.
Shell implementation: typed history/summary API, responsive panel, resource detail, local historical receipt compatibility, passive-chat filtering, late receipt/completion invalidation and periodic recovery while open. Targeted tests cover narrow layout, account/workspace switch, paging retry, dedup and late receipts.
Review: fixed mixed-cost label priority and missing completion invalidation. Backend interrupted-run retry now updates the same row, preserves its paging position, and retains cumulative token usage; regression E2E passed.

Verification: full shell suite 78 passed before one additional local-history regression (all 3 chat tests subsequently passed); focused collection tests 9 passed; targeted analysis clean; Flutter web build succeeded; rendered panel checked. Compute unit suite 34 passed / 1 optional PostgreSQL skip; focused agent unit suite 6 passed; receipt/retry E2E 2 passed and agent/security E2E 4 passed. Broader suites retain unrelated failures: UI registry count assertion (30 expected, 29 registered), and four application unit tests requiring absent repository files. No running application was stopped or deployed.

Final review: retry timestamp mapped to stable stored timestamp; mixed priced/unpriced model usage now produces an unreported preview. Regression reproduced before fix; final focused agent Release suite 7 passed.
