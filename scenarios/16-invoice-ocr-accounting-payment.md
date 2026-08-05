# Scenario 16: Invoice OCR → accounting → payment proposal

## User intent
Owner forwards or photographs a vendor invoice PDF/image. The brain extracts line items, matches PO/vendor in accounting, codes GL accounts, and proposes payment — execution only after approval.

## Trigger
Email attachment `EmailReceived` with PDF, or shell upload `ChatAttachmentAdded` / `DocumentIngested`.

## Imagined modules
- Documents/OCR
- Accounting (QuickBooks/Xero imagined)
- Purchasing/PO match
- Approvals
- Payments
- Gmail
- Chat / Shell

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| documents/owner | OCR + structured invoice parse |
| ap/owner | Vendor bill draft, GL coding |
| purchasing/owner | PO match |
| approvals/owner | Payment / bill approval |
| payments/owner | Execute payment rails |
| gmail/owner-inbox | Source or vendor comms |
| chat/owner-desk | Human review cards |
| shell/primary | Invoice review pane |

## Synapse choreography
1. Source **broadcasts** `DocumentIngested` (blobRef, mime) or email path extracts attachment → same fact.
2. Documents **broadcasts** `InvoiceParsed` (vendor, total, currency, lines[], invoiceNumber, dueDate, confidence).
3. AP **directs** `VendorMatchAsked` / `PurchaseOrderMatchAsked` → answers with match confidence.
4. AP **broadcasts** `BillDraftProposed` (glCodes[], tax, matchStatus).
5. Low confidence lines → chat **broadcasts** `HumanCodingRequired`; owner edits → `BillDraftCorrected`.
6. Owner approves bill → `ApprovalDecisionRecorded` → AP **directs** `BillCreateAsked` → accounting → `BillCreated`.
7. If due soon: AP **broadcasts** `PaymentProposed` (amount, method, schedule).
8. Separate approval for pay → payments `PaymentExecuteAsked` → `PaymentExecuted` or `PaymentScheduled`.
9. Gmail may `EmailSent` remittance advice.
10. Dashboard metrics may hear `PaymentExecuted` for cash tile (scenario 10 style).

## Orleans / Core surface exercised
DurableGrain journals; outbox; serialized AP turns; reminders for due dates; grain call filters; request context; streams optional for document pipeline stages; module catalog; approval deferral pattern.

## Rich experience
Side-by-side PDF and extracted fields; line table editable; PO match green/yellow/red; payment schedule control; audit trail of corrections.

## Failure / adversarial cases
- Duplicate invoiceNumber: `DuplicateInvoiceDetected` — do not double create bill.
- OCR misread amount: approval must show raw vs parsed; payment uses approved amount only.
- Accounting API retry double-create: idempotency keys.
- Vendor bank change social engineering: force re-approval on payment destination change `PaymentDestinationChanged`.
- Cross-owner document blob access denied.

## Capability claim
Invoice handling is a journaled extract→match→approve→pay pipeline with human gates — not an LLM that "remembers" it paid a vendor.
