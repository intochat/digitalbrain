# Scenario 22: Crypto wallet watch → tax lot journal

## User intent
Owner tracks a wallet address for swaps and transfers; each on-chain event becomes journaled tax lots / disposals with cost basis method (FIFO), dashboard updates, and a year-end export — human confirms ambiguous classifications (gift vs sale).

## Trigger
Chain indexer webhook / poll: `OnChainTransferObserved` for watched address; optional schedule `TaxLotReconcileDue`.

## Imagined modules
- CryptoWallet watch
- CryptoMarket (prices at timestamp)
- TaxLots
- CryptoDashboard
- Approvals (ambiguous class)
- Export
- Shell / Chat alerts

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| walletwatch/owner-main | Observe transfers/swaps |
| cryptomarket/spot | Historical price asks |
| taxlots/owner | Lots, disposals, method |
| cryptodashboard/owner-six | Portfolio points (ties scenario 02) |
| approvals/owner | Classify ambiguous |
| export/owner | CSV/PDF year package |
| chat/alerts | Large movement alerts |

## Synapse choreography
1. Wallet **broadcasts** `OnChainTransferObserved` (txHash, asset, amount, direction, counterparty, blockTime).
2. Taxlots hears → **directs** `HistoricalPriceAsked` → market → price at blockTime.
3. If clear buy: **broadcasts** `TaxLotOpened` (lotId, basis, qty).
4. If clear dispose: **broadcasts** `TaxLotDisposed` (proceeds, gains, method=FIFO, consumedLots[]).
5. If swap: composite facts `TaxLotDisposed` + `TaxLotOpened` with same txHash correlation.
6. Ambiguous (transfer to unknown personal wallet): **broadcasts** `TaxClassificationNeeded` → approval/decision UI → `TaxClassificationRecorded` (gift|self-transfer|sale).
7. Dashboard **broadcasts** `ChartPointAppended` / balance tiles from confirmed lots.
8. Year-end: `TaxExportAsked` → `TaxExportReady` with all lot refs.
9. Large inbound: chat `AssistantCardRendered` alert with tx link.

## Orleans / Core surface exercised
DurableGrain journals as tax books of record; reminders for reconcile; streams for chain events; serialized taxlots neuron (critical for FIFO ordering); outbox; request context; idempotency on txHash; grain call filters; placement sticky for taxlots.

## Rich experience
Portfolio chart; lots table; disposal explainer (which lots consumed); classify modal; export button; link out to block explorer.

## Failure / adversarial cases
- Out-of-order chain events: buffer by blockTime with reorg handling `OnChainReorgDetected` invalidates non-finalized facts.
- Double webhook: dedup txHash+logIndex.
- Price API gap: mark `PriceMissing`; do not invent basis.
- FIFO race with concurrent txs: single serialized taxlots turn queue.
- Reclass after export: `TaxClassificationAmended` new fact; exports versioned.

## Capability claim
On-chain events become owner-scoped, ordered, amendable tax journals with dashboard projections — a nervous system for finance, not a spreadsheet the chatbot cannot reliably update.
