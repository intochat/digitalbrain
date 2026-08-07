# Edge, Salesforce Reader, and Flutter Delivery Addendum

**Status:** Approved delivery addendum — 2026-08-07
**Basis:** The approved V2 product architecture and the implementation handoff.

## Scope

This addendum implements three already-approved follow-on slices without
changing the Kernel or introducing a second product core:

1. a durable workspace approval inbox plus an Edge-owned Base UI Kit contract
   and opaque action bridge;
2. a workspace-bound Salesforce closed-won revenue reader; and
3. a Flutter renderer that consumes only the Edge schema.

## Boundaries

`DigitalBrain.Product.Approvals` gains a redacted, durable workspace inbox
read model. Individual approval neurons direct safe pending, lifecycle, and
outcome-uncertain updates to the fixed inbox identity. The inbox owns only
frozen review data and UI-independent lifecycle state; it never stores an
action binding, provider credential, or workspace value in a product fact.

`DigitalBrain.Product.Presentation` projects that read model into
renderer-neutral approval workspace surfaces. A deterministic opaque action
reference is derived from the frozen proposal identity, fingerprint, and one
of the two fixed decisions. The reference contains no Salesforce mutation
binding. Its scope is enforced by the workspace-bound Edge channel, and an
Edge authorization interface is retained for the later Access policy layer.

`DigitalBrain.Edge` is a non-Orleans adapter. It reads only known presentation
surface journals through a supplied `WorkspaceChannel`, emits a closed Base UI
Kit schema, and accepts only an opaque action reference. It resolves that
reference against the workspace's durable presentation snapshot before
publishing the existing `ApprovalDecisionSubmitted` ingress. It cannot accept
or construct a raw provider mutation binding. Presentation remains free of
HTTP and Flutter concerns.

`DigitalBrain.Adapters.Salesforce` implements `ISalesRevenueReader` without
putting Salesforce data into Sales Insights state. Hosting supplies a
workspace-bound connection store and factory. The adapter constructs the
single supported typed SOQL query, paginates only to the existing source-record
limit, refreshes after a stale query token once, and maps every raw response to
a redacted reader failure. The Sales Insights effect maps those reader
failures to semantic unavailable states; no provider message, token, SOQL, or
connection detail reaches a UI fact.

`clients/flutter/renderer` is a standalone Flutter package. It models and
renders the Edge JSON schema with a fixed set of Material/Base UI Kit widgets.
It gets snapshots and sends opaque action references through a `UiSurfaceClient`;
it never reads a journal, product fact, or provider payload. Restoration saves
the selected tab, drawer item, and last acknowledged snapshot revision.

## Durable and safety rules

- The fixed inbox identity is physical-workspace scoped by Hosting. Direct
  updates therefore cannot cross workspaces, and recorded direct-target
  snapshots retain recovery semantics.
- A chat review retains its frozen opaque chat context and has chat, drawer,
  and inbox placement. A webhook review has inbox placement only.
- Approval statuses are `Pending`, `Approved`, `Rejected`, `Expired`, and
  `MutationUncertain`. `MutationUncertain` is an execution outcome layered on
  an already approved proposal; it never retries or changes the frozen action.
- An Edge action publication is only an admission attempt. The visible final
  status comes back from the durable approval lifecycle/read model.
- A duplicate opaque action has a stable decision identity and cannot cause a
  second provider mutation. An action reference from another workspace cannot
  be resolved by that workspace's snapshot.
- The Salesforce adapter never accepts free-form SOQL, workspace input from a
  caller, or a renderer-supplied date range. It receives the existing typed
  `SalesQuery` and a Hosting-bound connection only.

## Verification intent

Tests prove replay/reconnect content and placement, duplicate action behavior,
cross-workspace isolation, action opacity, reader query and failure mapping,
adapter composition, and Flutter widget/action round trips. Normal tests use a
controlled HTTP handler and no live Salesforce credential. A live connection
remains an external deployment configuration.
