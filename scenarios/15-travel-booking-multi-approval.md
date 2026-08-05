# Scenario 15: Travel booking multi-step with policy approvals

## User intent
Owner asks to book a trip to a customer site (flights + hotel + calendar holds + expense draft) within company travel policy. Out-of-policy choices need manager approval; booking commits only after gates pass.

## Trigger
Chat: "Book me to Austin next Tue–Thu for Acme onsite, prefer morning flights under policy."

## Imagined modules
- Travel/Booking (GDS or OTA adapter imagined)
- Policy/TravelPolicy
- Approvals
- Calendar
- Expenses
- Gmail (itinerary)
- Chat / Shell

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | Intent capture |
| travel/owner | Search, hold, book |
| travelpolicy/org | Evaluate offers vs policy |
| approvals/owner | Manager grants |
| calendar/owner | Travel blocks |
| expenses/owner | Draft expense report |
| gmail/owner-inbox | Itinerary delivery |
| shell/primary | Offer comparison UI |

## Synapse choreography
1. `UserMessaged` → assistant **directs** `TravelSearchAsked` → travel (air+hotel windows).
2. Travel answers `TravelOffersAnswered` (ranked offers); **broadcasts** `TravelOffersPresented`.
3. For top picks: **directs** `TravelPolicyEvaluateAsked` → policy → `TravelPolicyEvaluated` (in|out, reasons[]).
4. Owner selects out-of-policy hotel → **broadcasts** `TravelSelectionMade`; **directs** `ApprovalBundleAsked` (manager scope).
5. Manager (other device/user binding) records `ApprovalDecisionRecorded`.
6. Travel **directs** `TravelHoldAsked` → supplier → `TravelHoldPlaced` (expires).
7. Reminder before hold expiry: `TravelHoldExpiring` **broadcast**.
8. On final confirm: `TravelBookAsked` → `TravelBooked` (pnr, hotelConf).
9. Calendar **directs** creates for flights/onsite → `CalendarEventCreated` × N.
10. Expenses **broadcasts** `ExpenseDraftCreated`; gmail **broadcasts** `EmailReceived` itinerary or sends confirmation `EmailSent`.
11. Chat `AssistantResponded` with itinerary card; correlation ties whole saga.

## Orleans / Core surface exercised
Reminders/timers for hold expiry; DurableGrain journals saga steps; multi-turn deferred approvals; request context; outbox durability (book then calendar order); grain call filters (policy); serialized travel neuron; pub-sub for offers; no distributed DB transaction — journaled compensation `TravelCancelAsked` if calendar fails after book.

## Rich experience
Offer table (price, policy badge, duration); seat map optional; approval status chip; itinerary timeline; "rebook" action.

## Failure / adversarial cases
- Hold expires during approval: booking path re-searches; cannot book stale holdId.
- Double book on retry: idempotency key on `TravelBookAsked`.
- Policy service stale: fail closed to re-evaluate before book.
- Manager is traveler: policy may require alternate approver — `ApprovalRoutingFailed`.
- Partial supplier success (flight ok hotel fail): compensating cancel + `TravelBookingPartialFailed` visible.

## Capability claim
Travel is a durable multi-party saga of offers, policy, approval, hold, book, and calendar — not a chat model that pretends a PNR exists.
