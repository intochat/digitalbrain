# Scenario 10: Live dashboard subscription from a stream of facts

## User intent
Owner pins a "Revenue pulse" dashboard that stays live: every closed-won Salesforce opportunity, every paid Stripe invoice (imagined), and every large inbound email mentioning "PO" updates KPI tiles and a rolling chart without refreshing the page or asking chat.

## Trigger
Shell: open/pin dashboard scene once; establishes durable subscription interest. Ongoing external facts drive updates.

## Imagined modules
- Shell UI (dashboard scene, SSE/widget bind)
- Salesforce
- Payments/Stripe (imagined)
- Gmail
- MetricsProjector (aggregate tiles)
- Charting
- Streams bridge (Orleans streams → UI edge)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| shell/primary | Scene + widget instances |
| metrics/owner-revenue | Aggregate KPIs; emit tile revisions |
| salesforce/owner-org | `OpportunityClosedWon` ambient |
| payments/owner | `InvoicePaid` ambient |
| gmail/owner-inbox | `PurchaseOrderEmailDetected` |
| charting/default | Rolling series |
| uiedge/owner-session | Push widget patches to clients |

## Synapse choreography
1. Owner opens scene → **broadcasts** `SceneOpened` (scene=revenue-pulse); widgets **broadcast** `DashboardSubscriptionAttached` (metrics/owner-revenue, series keys).
2. Metrics neuron on attach **broadcasts** `DashboardSnapshot` (tiles, chart bootstrap).
3. Salesforce **broadcasts** `OpportunityClosedWon` (amount, account, oppId).
4. Metrics hears → updates state → **broadcasts** `KpiTileUpdated` (tile=closedWonToday, value, rev) and `ChartPointAppended` (series=revenue, t, amount).
5. Payments **broadcasts** `InvoicePaid` → same metrics path → tiles/cash chart.
6. Gmail classifier **broadcasts** `PurchaseOrderEmailDetected` → metrics **broadcasts** `KpiTileUpdated` (tile=openPOs) + optional `DashboardAnnotationAdded`.
7. `uiedge/owner-session` listens to tile/chart facts (stream or grain observer) → pushes patches to Flutter/web shell.
8. Owner leaves and returns: new session **directs** `DashboardSnapshotAsked` → metrics answers latest revision; subscription re-attaches from revision watermark.

## Orleans / Core surface exercised
Streams (explicit consumer for dashboard); durable subscriptions / reminders to renew; DurableGrain journals for metrics revisions; stateless workers optional for classification; pub-sub; watchers/observers for UI edge; outbox durability; placement of metrics grain; grain versioning if projector logic upgrades mid-day.

## Rich experience
Live tiles (pulse animation on update); rolling 24h chart; activity feed of last 20 facts with module icons; quiet hours toggle emits `DashboardMuteSet`.

## Failure / adversarial cases
- UI reconnect storm: snapshot+watermark, not full replay of day.
- Double stream delivery: metrics dedup by source synapseRef before bumping revision.
- Backpressure: drop or coalesce high-frequency points with `ChartPointsCoalesced` fact rather than unbounded UI flood.
- Owner isolation: session edge must not subscribe to another owner's metrics neuron.
- Projector crash: restart rebuilds from journaled inputs or checkpoint; tiles never only in RAM without recovery path.

## Capability claim
The shell is a live subscriber to the synapse stream — dashboards are nervous-system projections, not polling chat prompts.
