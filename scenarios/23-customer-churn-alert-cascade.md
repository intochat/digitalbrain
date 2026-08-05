# Scenario 23: Customer churn risk cascade

## User intent
When signals align (support ticket spike, champion leaves LinkedIn/X, usage drop webhook, negative email sentiment), the brain raises a churn risk case, pages the owner, proposes a save play (exec email, discount opp, CS task), and tracks outcome — without waiting for the owner to ask "any customers at risk?"

## Trigger
Composite: any single strong signal or scored combination crosses threshold (streaming evaluations).

## Imagined modules
- Support/Tickets
- Gmail sentiment
- Product usage (imagined)
- X/LinkedIn watch (champion)
- ChurnRisk engine
- Salesforce
- Tasks / Gmail / Chat
- Shell dashboard

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| tickets/owner | Ticket surge facts |
| gmail/owner-inbox | Sentiment on account threads |
| usage/owner | UsageDropped facts |
| xwatch/champions | Champion job-change posts |
| churn/owner | Score cases; propose plays |
| salesforce/owner-org | Account health fields |
| chat/alerts | Page owner |
| shell/primary | Risk board |

## Synapse choreography
1. Ambient: `SupportTicketOpened`, `EmailSentimentScored`, `UsageDropped`, `ChampionSignalObserved` **broadcast** from modules.
2. Churn neuron hears, updates score, **broadcasts** `ChurnRiskScoreUpdated` (accountId, score, factors[]).
3. Crossing threshold → **broadcasts** `ChurnCaseOpened` (caseId, accountId, severity).
4. Chat/shell alert `AssistantCardRendered` / push.
5. Churn **broadcasts** `SavePlayProposed` (options: execEmail, qbr, discount, successPlan).
6. Owner picks → approvals if discount → SF `OpportunityCreateAsked` or `AccountHealthPatched`.
7. Gmail exec note `EmailSendAsked`; tasks `TaskCreateAsked` for CS.
8. Outcome later: `ChurnCaseClosed` (retained|churned) with Cause links to play facts.
9. Metrics dashboard tiles listen to open case counts.

## Orleans / Core surface exercised
Streams for high-volume usage; DurableGrain journals; pub-sub; timers for score decay; serialized churn case updates; request context; grain call filters; outbox; watchers for risk board; optional stateless scoring workers with case neuron as authority.

## Rich experience
Risk board kanban; factor chips with evidence links; one-click save plays; account 360 pane; severity color pulse.

## Failure / adversarial cases
- Alert fatigue: hysteresis + cooldown `ChurnAlertSuppressed`.
- Wrong account attribution on email domain: require SF account match confidence.
- Double-open case: one open case per accountId.
- Play executes after customer already cancelled: re-read SF stage before send.
- Scoring worker vs case neuron split brain: only case neuron emits `ChurnCaseOpened`.

## Capability claim
The OS proactively opens and runs a multi-module save play from ambient business facts — initiative without a user prompt, still gated and journaled.
