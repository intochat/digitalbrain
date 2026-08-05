# Scenario 48: Ask “show me why sales dropped” with multi-chart answer

## User intent
The owner asks a diagnostic question. The brain should pull CRM pipeline, product usage, support ticket volume, and marketing spend; then answer with a narrative plus multiple coordinated charts and a “likely drivers” table with follow-up actions.

## Trigger
Chat: “Show me why sales dropped last month.”

## Imagined modules
- Assistant / analytics planner
- CRM metrics
- Product analytics
- Support desk stats
- Marketing spend
- ChartBuilder × multi
- Causal narrative helper (model)
- Tasks for follow-ups

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / analytics | Question |
| SalesDiagnostic / month-7 | Fan-out orchestrator |
| CrmMetrics / default | Pipeline/won |
| ProductMetrics / default | Usage/activation |
| SupportMetrics / default | Ticket volume |
| MarketingMetrics / default | Spend |
| ChartBuilder / month-7 | Multiple ChartSpecs |
| UiProjector / shell | Multi-chart canvas |
| TaskStore / personal | Optional follow-ups |

## Synapse choreography
1. UserMessaged → SalesDiagnostic start (`DiagnosticStarted`).
2. Parallel Asks to four metric modules; join on journal.
3. Emit progressive charts as each answer arrives (`UiSurface` updates).
4. When complete, Ask model capability `ExplainDrivers(structured metrics)` with **numbers from facts**, not free browsing.
5. Emit `AssistantResponded` + multi-chart layout + table of drivers + buttons `CreateInvestigationTasks`.
6. Owner tap creates tasks via Confirm.
7. Correlation links all charts to the same diagnostic id for replay (scenario 34).

## Orleans / Core surface exercised
Fan-out/fan-in; DurableGrain journals; Ask/Answer/Continue; UI progressive surfaces; stateless workers if heavy aggregations; request context; outbox; optional streams for metric ticks.

## Rich experience
Canvas: 2×2 charts (revenue, win rate, tickets, spend), narrative sidebar, driver table with sparklines, actions “Drill into segment”, “Open CRM list”, “Export pack”.

## Failure / adversarial cases
- One metric source down → show three charts + degraded card; don’t refuse whole answer.
- Model invents a driver not in metrics → UI citations require metric fact ids.
- Double diagnostic on repeat ask → new id; don’t mutate old canvas silently.
- Heavy queries timeout → AskExpired per source; partial join rules explicit.

## Capability claim
DigitalBrain can answer causal business questions as multi-module, multi-chart durable diagnostics—not a single LLM guess without tool-grounded series.
