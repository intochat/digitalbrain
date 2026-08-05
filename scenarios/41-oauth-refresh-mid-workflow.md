# Scenario 41: Permission/OAuth refresh mid-workflow

## User intent
Mid multi-step “file expense from email + attach receipt to Drive + log in finance app,” an OAuth token expires. The owner should get a precise re-auth surface, resume the same workflow from journaled state, and not restart from scratch or double-submit the expense.

## Trigger
External API returns 401 during an in-flight capability; module Emits `AuthorizationRequired` / token refresh flow.

## Imagined modules
- Gmail, Drive, FinanceApp connectors
- OAuth / token broker
- ExpenseWorkflow behavior
- UiEdge authorization feed
- Outbox

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| ExpenseWorkflow / exp-19 | Multi-step join state in journal |
| GmailConnector / default | Fetch message/receipt |
| DriveConnector / default | Upload |
| FinanceConnector / default | Create expense |
| OAuthBroker / owner | Refresh / interactive auth |
| UiAuth / shell | Modal re-auth |
| Chat / exp-19 | User messaging for auth |

## Synapse choreography
1. Workflow Asks Gmail success; Ask Drive upload starts.
2. Drive returns failure classified as `AuthorizationExpired(scope=drive.file)`.
3. Connector Emits `AuthorizationRequired(correlation, scopes)` broadcast to UI and OAuthBroker.
4. ExpenseWorkflow journals pause (`WorkflowPaused`); does not invent success.
5. Owner completes OAuth in shell; OAuthBroker Emits `AuthorizationGranted(scopes)`.
6. ExpenseWorkflow hears grant matching correlation → Continues: retry Drive upload once, then Finance create.
7. Completes with `ExpenseFiled` + UI; prior Gmail fetch not redone if journal already holds receipt blobRef.

## Orleans / Core surface exercised
DurableGrain journals as workflow resume state; outbox; AskExpired vs auth pause distinction; request context; grain call filters attaching auth; reminders to nudge owner if paused too long; no distributed transaction—compensating facts.

## Rich experience
Non-blocking modal “Reconnect Google Drive”; workflow card shows step checklist (Gmail ✓, Drive ⏸, Finance ·); after reconnect, auto-progress animation; deep link from email notification.

## Failure / adversarial cases
- Double AuthorizationGranted → single resume (journal epoch).
- User denies scopes → `AuthorizationDenied` terminal; clean cancel, no partial finance entry.
- Token refresh race across connectors → OAuthBroker serializes refresh per account.
- Malicious page emitting AuthorizationGranted → only OAuthBroker/edge may emit; filters enforce.

## Capability claim
DigitalBrain can pause and resume multi-connector workflows on real auth boundaries with journaled checkpoints—chatbots typically fail the whole tool chain and forget mid-state.
