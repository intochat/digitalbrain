# Scenario 04: "Why did you do it this way?" → prior instruction recall

## User intent
After the assistant takes an action (e.g. short email tone, skipped CC, used a specific Salesforce field), the owner asks why; the brain must retrieve the prior user instruction or policy fact that justified the choice, with journal links — not a post-hoc rationalization.

## Trigger
Chat message in the same or a later thread: `UserMessaged` "Why did you do it this way?" referring to the last consequential action (or an explicit action id).

## Imagined modules
- Chat / Assistant
- Introspection (cause-chain walk)
- Memory (standing instructions, preferences)
- Policy/Guardrails (optional compiled rules)
- Gmail or Salesforce (the module that acted)
- Shell UI (cause graph widget)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | Dialogue; bind "this" to last action |
| assistant/owner | Decision emitter; journals capability selections |
| memory/instructions | Store/recall standing `UserInstructionCaptured` |
| introspection/cause | Walk Cause/Answers chain across journals |
| gmail/owner-inbox | Example actor that applied tone policy |
| shell/primary | Render cause chain visualization |

## Synapse choreography
1. Earlier (setup): owner said "Always keep outbound sales email under 80 words." → chat **broadcasts** `UserInstructionCaptured` (instructionId, text, scope=email.outbound); memory hears and indexes.
2. Later action: assistant plans send → **broadcasts** `CapabilityToolSelected` / module **broadcasts** `EmailSendProposed` with `justificationRef` → instruction synapseRef; then `EmailSent`.
3. Owner: `UserMessaged` why-question.
4. Chat **directs** `ActionReferentResolvedAsked` (thread focus / last outbound) → introspection answers `ActionReferentResolved` (targetSynapseRef).
5. Chat **directs** `CauseExplainAsked` → `introspection/cause` (targetSynapseRef).
6. Introspection walks journal metadata Cause/Answers, **directs** reads, answers `CauseExplainAnswered` (chain[], policyHits[], instructionRefs[]).
7. If chain cites `UserInstructionCaptured`, memory may **answer** `InstructionBodyAsked` for full text.
8. Chat **directs** `AssistantResponded`: "Because on {date} you said …" with quote + links; **broadcasts** `ExplanationRendered` (chain for UI).
9. If no justifying instruction exists: respond with actual cause (model default / module default) and offer `CaptureInstructionSuggested` rather than fabricating a user rule.

## Orleans / Core surface exercised
DurableGrain journals + SynapseMetadata Cause/Answers; request context correlation; serialized turns; grain call filters; module catalog; watchers/observers optional for live cause pane; outbox durability so explanation reads committed facts only.

## Rich experience
Chat quote-block of the prior instruction; expandable cause chain (tool select → proposal → send); shell graph nodes clickable to journal lines; action "Pin as standing rule" if missing.

## Failure / adversarial cases
- Fake rationale: Core/modules must not invent instructionRefs; absence is a first-class answer.
- Stale instruction: superseded instructions journal `UserInstructionSuperseded`; explainer prefers latest effective scope.
- Cross-thread ambiguity: referent resolution must ask clarifying `AmbiguousReferent` rather than pick another owner's or another chat's action.
- Instruction scope leak: email instruction must not justify Salesforce field wipe.
- Re-explaining after rewrite: explanations key off immutable synapseRef, not mutable UI copy.

## Capability claim
The system can cite the owner's earlier instruction as a journaled cause of a later action — accountability through synapse provenance, not chatbot improvisation.
