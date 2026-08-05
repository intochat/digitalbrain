# Scenario 06: Rich chat — image in, sales chart out

## User intent
Owner drops a photo of a whiteboard pipeline sketch (or a CSV screenshot) into chat and asks for a cleaned sales funnel chart against live Salesforce stage counts, rendered inline with a downloadable table.

## Trigger
Chat message with image attachment: `UserMessaged` + `ChatAttachmentAdded` (image/png).

## Imagined modules
- Chat (multimodal turn, cards)
- Vision/OCR (image → structured stages)
- Salesforce (opportunity stage aggregation)
- Charting (series → render model)
- Shell UI (inline image + chart widgets)
- Memory (last chart prefs)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | Multimodal turn host |
| vision/default | OCR/structure extraction from image |
| salesforce/owner-org | Aggregate opps by stage |
| charting/default | Build chart specs from tables |
| shell/primary | Render attachment + chart panes |
| memory/owner-profile | Chart color/theme prefs |

## Synapse choreography
1. Edge **directs** `UserMessaged` (text) and **broadcasts** `ChatAttachmentAdded` (blobRef, mime, width, height) with same correlation.
2. Chat **directs** `VisionExtractAsked` → vision (blobRef, schemaHint=sales_stages).
3. Vision answers `VisionExtractAnswered` (stages[], rawText, confidence).
4. Chat **directs** `OpportunityStageStatsAsked` → Salesforce → `OpportunityStageStatsAnswered` (stage, count, amount).
5. Chat **directs** `ChartBuildAsked` → charting (merge whiteboard labels with SF stats) → `ChartBuildAnswered` (chartSpec: bar/funnel, series, table).
6. Chat **broadcasts** `ChatArtifactProduced` (chartSpec, tableRows, sourceRefs).
7. Chat **directs** `AssistantResponded` (caption + interpretation); shell/chat UI binds artifacts.
8. Optional owner action "pin to home" → `DashboardTilePinned` broadcast.

## Orleans / Core surface exercised
Serialized chat turns for whole multimodal chain; DurableGrain journals (attachment refs not raw bytes in journal if policy stores blob elsewhere — ref is the fact); request context; outbox durability; streams for large artifact notify; module catalog; grain call filters on Salesforce.

## Rich experience
Inline original image thumbnail; funnel/bar chart; data table (stage, count, $); actions: refresh from SF, export PNG/CSV, open Salesforce list view. Multi-pane: left transcript, right chart focus.

## Failure / adversarial cases
- OCR garbage: low confidence → ask clarifying stages rather than wrong chart.
- Attachment virus/oversize: reject before vision with `ChatAttachmentRejected`.
- SF timeout mid-turn: chart from image-only with banner "live counts unavailable"; no fake numbers.
- Double submit of same image: dedup attachment hash per chat turn.
- Cross-owner blob access: blobRef capability scoped to owner.

## Capability claim
One chat turn fuses multimodal input, CRM aggregation, and structured chart artifacts as journaled synapses — not a text-only bot that pretends a chart exists.
