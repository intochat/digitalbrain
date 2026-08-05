# Scenario 29: Long-running research with progressive UI updates

## User intent
The owner asks: “Compare our last four quarters of pipeline vs closed-won and summarize competitive mentions in call notes.” They want a multi-minute research job that keeps the UI alive—progress bars, partial tables, intermediate charts—while the chat remains usable for other questions in other contexts.

## Trigger
Chat message `UserMessaged` with research intent; Assistant selects ResearchOrchestrator capability.

## Imagined modules
- Chat / Assistant
- ResearchOrchestrator behavior
- CrmConnector (Salesforce/pipeline)
- TranscriptCorpus (call notes)
- VectorMemory
- ChartBuilder
- UiProjector / multi-pane shell
- Pulse / progress ticks (optional)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / research-q1 | Holds user turn; does not block silo-wide |
| ResearchOrchestrator / job-8841 | Fan-out asks; progressive UiSurface emits |
| CrmFacts / default | Answers GetPipelineSlice |
| TranscriptSearch / default | Answers SearchCallMentions |
| EmbedWorker (stateless) | Batch embeds note chunks |
| ChartBuilder / job-8841 | Emits ChartSpec surfaces |
| UiProjector / shell | Multi-pane: chat + research canvas |
| JobPulse / job-8841 | Optional Tick for timeout/progress |

## Synapse choreography
1. Chat broadcasts `UserMessaged`; Assistant emits directed `CapabilityRequested(Research)` / ResearchOrchestrator hears job start.
2. ResearchOrchestrator Emits `ResearchStarted` + `UiSurface(Progress 0%)` (broadcast to UI).
3. Fan-out: Ask `GetPipelineSlice(Q1..Q4)`, Ask `SearchCallMentions(competitors)`—non-blocking; join via own journal (Continue pattern).
4. As each `Answer` arrives, orchestrator Emits `ResearchPartial(table|chart)` and updated `UiSurface`—still no blocking wait across neurons.
5. EmbedWorker stateless fan-out for large note sets; results return as directed answers with correlation.
6. Final `ResearchCompleted` + `AssistantResponded` (directed to chat) with summary text referencing pane widgets.
7. Other chat contexts continue in parallel; this job’s instance name isolates state.

## Orleans / Core surface exercised
Serialized turns per neuron; Continue/Ask non-blocking; DurableGrain journals as join state; stateless workers; streams or outbox for partial UI; reminders/timers for job timeout (`AskExpired` / `Schedule`); request context for job id.

## Rich experience
Split shell: left chat transcript, right research canvas with progressive table fills, sparkline charts, “sources” chips; cancel button carrying `CancelResearch` synapse; toast on completion with deep link to journal.

## Failure / adversarial cases
- Partial UI without final journal commit → restart must rebuild progress from journal, not invent completion.
- Double final summary if two answers race the join → journal-as-counter must emit `ResearchCompleted` once.
- Reentrancy if UI tap calls back into orchestrator mid-turn → taps enqueue as new facts.
- Token/context overflow on model summary step → CapabilityCompleted without AssistantResponded must surface as error surface, not silence.

## Capability claim
DigitalBrain can run long, multi-module research as durable fact choreography with progressive UI, while a normal chatbot only streams tokens from a single opaque tool call.
