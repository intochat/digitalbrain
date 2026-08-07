# Sales Insights Implementation Plan

> **For agentic workers:** Execute test-first in this workspace. No Git state may be inspected or changed.

**Goal:** Deliver one durable chat-to-sales-insight path that emits a semantic
daily chart/table surface for a closed-won revenue query.

**Architecture:** Sales Insights owns correlated query state, record validation,
and daily aggregation. An injected reader is the provider seam. Presentation
owns the renderer-neutral surface, while Conversation only translates a trusted
chat command into a directed start.

**Tech Stack:** .NET 11, DigitalBrain durable neurons, xUnit v3 composed-host
integration tests.

## Global constraints

- Use explicit half-open `DateOnly` ranges of at most 366 days, at most 10,000
  reader records, and a single ISO-style currency code.
- Never place workspace scope, credentials, raw provider query, or executable
  action in a product or presentation fact.
- Reader failures and invalid provider data are explicit unavailable outcomes;
  they are never zero-value charts.
- Do not use Git commands or mutate Git state.

---

### Task 1: Typed vocabulary and durable query state

**Files:**

- Create `src/DigitalBrain.Product.SalesInsights/*` for query/value objects,
  reader seam, state, facts, state neuron, and effect neuron.
- Modify `DigitalBrain.slnx` and the product test project references.
- Test `src/DigitalBrain.Product.Tests/SalesInsights/SalesInsightsTests.cs`.

**Consumes:** a `ChatSalesRequested` translated into
`SalesInsightRequested(SalesQuery, SalesInsightContext)`.

**Produces:**

```csharp
Task<IReadOnlyList<SalesRevenueRecord>> ReadClosedWonAsync(
    SalesQuery query, CancellationToken cancellationToken);
```

and exactly one `SalesInsightReady` or `SalesInsightUnavailable` per query id.

- [x] Write a composed-host failing test for reader records becoming seven ordered
  calendar buckets, a 225 USD total, and three deals.
- [x] Run the test and observe the missing Sales Insights feature.
- [x] Add only the typed vocabulary/state/effect needed for that test.
- [x] Run the focused test and observe the chart-domain result.

### Task 2: Trusted chat translation and semantic projection

**Files:**

- Modify `src/DigitalBrain.Product.Conversation/ConversationIngressNeuron.cs`.
- Create chat sales vocabulary in Conversation.
- Create `SalesInsightProjectionNeuron` and semantic surface vocabulary in
  `src/DigitalBrain.Product.Presentation`.
- Extend the focused integration test.

**Consumes:** `ChatSalesRequested` from external ingress and
`SalesInsightReady` from a matching Sales Insights neuron.

**Produces:** a `SalesInsightSurfaceRequested` with opaque chat context,
`Chat`/`ContextDrawer` placements, `BarChart`/`Table` display hints, and no
provider/action/scope fields.

- [x] Write the failing chat-to-surface assertion with hand-derived data.
- [x] Run it and observe no projection.
- [x] Implement only source provenance checks and the projection.
- [x] Run the focused test and observe the semantic surface.

### Task 3: Unavailable and idempotence behavior

**Files:**

- Extend `SalesInsightsTests.cs` only when a production behavior is unprotected.
- Add redacted unavailable-surface projection if the success surface cannot
  express unavailable safely.

**Consumes:** reader exception/invalid records and duplicate trusted chat input.

**Produces:** one unavailable surface without exception data, or one frozen
completed insight; never a misleading zero chart or a second provider result.

- [x] Write the reader-failure test; expected result is an unavailable surface
  and absence of `SalesInsightSurfaceRequested`.
- [x] Write the duplicate-start test; expected result is one durable ready fact.
- [x] Run each focused test red, implement the minimum state/projection guard,
  then run both green.

### Task 4: Verify and record the first chart contract

**Files:**

- Update `CONTEXT.md` and
  `docs/superpowers/specs/2026-08-06-sales-insights-design.md` if verification
  discovers a terminology or contract discrepancy.

- [x] Run the Sales Insights class, all product tests, solution build, and full
  solution test suite.
- [x] Record exact pass/fail/skip results without claiming unsupported live
  Salesforce or renderer coverage.

Verification recorded on 2026-08-06:

- `SalesInsightsTests`: 7 passed, 0 failed.
- `DigitalBrain.Product.Tests`: 45 passed, 0 failed.
- `dotnet build DigitalBrain.slnx --no-restore`: 0 warnings, 0 errors.
- `dotnet test DigitalBrain.slnx --no-restore -- --timeout 120s`: 98 passed,
  0 failed, 3 explicitly filtered Qdrant container tests skipped.
