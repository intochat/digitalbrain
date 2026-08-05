# Scenario 38: Chart + image + interactive buttons in one assistant response

## User intent
The owner asks for a portfolio health check. The answer must be one coherent experience: short prose, a performance chart, a sparkline image or server-rendered PNG, a holdings table, and buttons (Rebalance proposal, Open broker, Dismiss).

## Trigger
Chat message about portfolio health.

## Imagined modules
- Assistant
- Portfolio module
- ChartBuilder
- ImageRender (optional PNG)
- Broker deep links
- UiProjector / Flutter widget union

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / money | Conversation |
| Assistant / money | Orchestrates asks |
| Portfolio / default | Positions, returns |
| ChartBuilder / money | ChartSpec facts |
| ImageRender / default | Renders chart image blob |
| UiProjector / shell | Composes multimodal surface |
| ActionRouter / shell | Maps button OnTap synapses |

## Synapse choreography
1. UserMessaged → Assistant fan-out Ask `GetHoldings`, `GetReturns(range)`.
2. On full join: Emit `AssistantResponded(text)` and broadcast `UiSurface(blocks: [Markdown, Chart, Image, Table, ButtonBar])`.
3. ChartBuilder may Emit intermediate `ChartSpec`; ImageRender Answers `RenderChart` with blobRef.
4. Buttons carry typed synapses: `ProposeRebalance`, `OpenExternal(broker)`, `DismissSurface`—taps are facts, not callbacks into controllers.
5. ActionRouter/Portfolio hears ProposeRebalance → new choreography with confirm gate.
6. Shell binds one correlation so all blocks clear/replace together on next turn.

## Orleans / Core surface exercised
Fan-out/fan-in join; DurableGrain journals; Ask/Answer; UI-as-facts; outbox for image blob; request context; pub-sub of UiSurface to multiple panes if needed.

## Rich experience
Single assistant turn card with embedded chart, table, image thumbnail lightbox, primary/secondary actions; accessibility labels from surface schema; dark/light chart themes.

## Failure / adversarial cases
- Text arrives, chart fails → partial surface with explicit ChartFailed block, not silent blank.
- Button double-tap → idempotent ProposeRebalance journal gate.
- Huge table freezes UI → pagination facts / virtualized table spec.
- Action synapse forged from another session → owner session checks on ActionRouter.

## Capability claim
DigitalBrain can ship multimodal, actionable UI as typed surfaces in the same causal turn as the assistant text—beyond markdown-only chatbots.
