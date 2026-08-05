# Scenario 33: Photo of whiteboard → structured tasks

## User intent
The owner snaps a photo of a whiteboard after a planning session. They want OCR/vision extraction into structured tasks with assignees and due dates, editable in UI, then saved to Tasks and optional calendar blocks.

## Trigger
Chat/widget image attach: `ImageAttached(mime, blobRef)` or camera capture in Flutter shell.

## Imagined modules
- Vision/OCR module
- Task structure LLM capability
- Tasks module
- Calendar module
- Blob store
- UiProjector (image + overlay + table)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / board-capture | Receives image |
| VisionIngress / default | Emits ImageStored, OcrRequested |
| OcrWorker (stateless) | Returns OcrText |
| BoardParser / default | Maps OCR → TaskCandidates |
| TaskStore / personal | Persists on confirm |
| Calendar / personal | Optional blocks |
| UiProjector / shell | Image pane + editable table |

## Synapse choreography
1. `ImageAttached` broadcast → VisionIngress stores blob, Emits `ImageStored`.
2. Ask `RunOcr(blobRef)` to OcrWorker (stateless worker); Answer `OcrText`.
3. BoardParser on Answer Emits `WhiteboardParsed`, `TasksProposed`, `UiSurface(ImageWithHighlights, TaskTable)`.
4. Owner edits cells (each edit is `TaskCandidateEdited` fact, not silent UI state).
5. `ConfirmTasks` → TaskStore `TasksCreated`; optional `ScheduleBlocksProposed` → Calendar.
6. `AssistantResponded` summarizes count and links; Memory may store board snapshot ref.

## Orleans / Core surface exercised
Stateless workers (OCR/embeddings-like CPU); DurableGrain journals; Ask/Answer continue; outbox for blob IO boundaries; placement for heavy workers; streams optional for large image progress.

## Rich experience
Two-pane: photo with bounding boxes, editable task table, confidence badges on low-OCR lines, actions “Create tasks”, “Add 30m calendar holds”, “Regenerate with hint”.

## Failure / adversarial cases
- OCR garbage → user edit trail must win; regenerating must not duplicate created tasks without new confirm.
- Huge image kills grain turn → chunk/progress facts; timeout AskExpired.
- PII on whiteboard projected to shared pane → collaborative share rules (see scenario 42).
- Double-tap Confirm → idempotent TasksCreated.

## Capability claim
DigitalBrain can make a camera capture a first-class multi-module pipeline with durable edits and confirms, not a one-shot vision reply in a chat bubble.
