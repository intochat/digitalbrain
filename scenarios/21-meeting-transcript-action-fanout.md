# Scenario 21: Meeting transcript → action-item fan-out

## User intent
After a customer call, a transcript lands (Zoom/Meet connector imagined). The brain extracts decisions and action items, assigns owners via contacts, creates tasks, drafts follow-up email, and updates Salesforce activity — owner confirms the packet once.

## Trigger
External `MeetingTranscriptReady` from conferencing module at meeting end.

## Imagined modules
- Conferencing/Transcript
- Assistant (extraction)
- Contacts
- Tasks
- Gmail
- Salesforce
- Approvals
- Chat / Shell

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| meetings/owner | Transcript ingress |
| assistant/owner | Extract decisions/actions |
| contacts/owner | Resolve assignees |
| tasks/owner | Create tasks |
| gmail/owner-inbox | Follow-up draft/send |
| salesforce/owner-org | Log activity / next steps fields |
| approvals/owner | Packet approval |
| chat/owner-desk | Review UI |

## Synapse choreography
1. Meetings **broadcasts** `MeetingTranscriptReady` (meetingId, transcriptRef, attendees[]).
2. Assistant **directs** fetch if needed → **broadcasts** `MeetingInsightsExtracted` (decisions[], actions[{text, ownerHint, dueHint}]).
3. For each action: `ContactResolveAsked` → assignee ids.
4. Assistant **broadcasts** `FollowUpPacketProposed` (tasks[], emailDraft, sfActivityDraft).
5. `ApprovalBundleAsked` for packet; owner edits one task then grants.
6. On grant: fan-out directed asks — `TaskCreateAsked` × N, `EmailSendAsked` or leave draft, `SalesforceActivityLogAsked`.
7. Each completion fact Cause-linked to packet; final `FollowUpPacketCompleted`.
8. Chat summary `AssistantResponded` with checklist of created artifacts.

## Orleans / Core surface exercised
DurableGrain journals; multi-ask fan-out with continuations; approval deferral; request context; outbox ordering; grain call filters; serialized assistant/orchestrator; pub-sub transcript ambient; reminders for action dues extracted.

## Rich experience
Transcript pane + insights pane; editable action table; email draft preview; SF activity checkbox; single Approve packet button.

## Failure / adversarial cases
- Partial fan-out failure: packet status `Partial`; retry per child idempotently.
- Wrong assignee guess: require owner confirm on low-confidence resolves.
- Transcript PII: retention policy + hold interaction (scenario 14).
- Duplicate transcript delivery: dedup meetingId.
- Reentrancy from task completion broadcasts back into orchestrator — design one-way completion handlers without calling into in-flight turn.

## Capability claim
Meetings become a confirmed fan-out of durable work artifacts across tasking, mail, and CRM from one insight packet — not a transcript abandoned in a docs folder.
