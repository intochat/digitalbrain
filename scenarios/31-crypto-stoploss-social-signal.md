# Scenario 31: Crypto stop-loss auto-action after social signal

## User intent
The owner configures: if a watched influencer posts panic language about a held asset *and* price crosses a trailing stop, sell a defined fraction and notify with a chart. They want automatic action with an audit trail they can defend later—not a manual “did you see Twitter?” loop.

## Trigger
Compound: SocialPostObserved broadcast + PriceTick stream; behavior rules installed earlier as a scripted neuron.

## Imagined modules
- SocialWatch (X/Twitter or similar)
- MarketData feed
- PortfolioBroker (exchange API)
- RiskPolicy behavior (owner-authored thresholds)
- ChartBuilder
- Notify / Chat push
- Secrets / OAuth for exchange

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| SocialWatch / handles | Emits SocialPostObserved |
| MarketPulse / btc-usd | Emits PriceTick / Stream |
| RiskPolicy / crypto-desk | Joins signal+price via journal; decides |
| PortfolioBroker / exchange | Answers PlaceOrder / hears ExecuteSell |
| ChartBuilder / crypto-desk | UiSurface chart |
| AlertChat / push | Assistant-style notification card |
| AuditVault / compliance | Overhears all trade facts |

## Synapse choreography
1. SocialWatch broadcasts `SocialPostObserved(author, text, assetHints)`.
2. RiskPolicy hears it; journals; may Ask `SentimentScore` (directed) without trading yet.
3. MarketPulse continuously broadcasts `PriceTick`; RiskPolicy hears ticks, updates trailing stop state only via journaled facts (`StopLevelAdjusted`), not silent fields alone.
4. When join condition holds, RiskPolicy Emits `StopLossTriggered` (broadcast) and directed `ExecuteSell(fraction, asset)` toward PortfolioBroker.
5. PortfolioBroker places order; replies `OrderFilled` / `OrderRejected`.
6. On fill: `UiSurface(Chart+FillSummary)`, `NotifyOwner`, AuditVault overhears.
7. If broker `DeliveryFailed`, self-heal path may Schedule retry or Emit `ManualInterventionRequired`.

## Orleans / Core surface exercised
Streams (price); DurableGrain journals as decision memory; Schedule/reminders for delayed rechecks; outbox durability for ExecuteSell; grain call filters (risk headers); serialized turns so two ticks don’t double-sell; transactions only if exchange module justifies local multi-grain atomicity—usually avoided at Core, compensated with saga facts.

## Rich experience
Live desk pane: social snippet, price chart with stop line, big “armed/triggered/filled” state, one-tap “disarm” button → `DisarmStopLoss` fact; mobile push with deep link to journal sequence proving why it sold.

## Failure / adversarial cases
- Double fill on at-least-once PriceTick → idempotency keys + journal gate before ExecuteSell.
- Social injection (“SELL EVERYTHING”) without price condition → policy must require join; policy neuron is mandatory.
- Reentrancy if OrderFilled handler emits something PortfolioBroker asks back same turn → deadlock risk.
- Secrets expired mid-order → OAuth refresh scenario; OrderRejected must not look like success in UI.

## Capability claim
DigitalBrain binds external signals, market streams, and irreversible actions into one journaled causal story an owner can audit—far beyond a chatbot that only “would recommend” a sell.
