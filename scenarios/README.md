# 50 user scenarios — Core stress set

> **Status:** historical stress material. It does not define the current Core
> surface. See [the current architecture](../CORE-ARCHITECTURE.md).

These scenarios assume **modules exist** (Gmail, Salesforce, X, crypto, chat, shell, behaviors, …).
They exist to force **Core shape**, not to implement product features.

Current architecture: [`../CORE-ARCHITECTURE.md`](../CORE-ARCHITECTURE.md).

## How to use

1. Read a scenario’s **Synapse choreography** — if Core cannot express the arrows, Core is wrong.
2. Read **Orleans / Core surface** — each feature must have a home in Core, not in a module hack.
3. Read **Failure / adversarial** — green path is cheap; these are the ratification tests.
4. **Capability claim** — what an ordinary chatbot cannot do.

## Index

| # | File | Theme |
|---|---|---|
| 01 | [01-gmail-websearch-salesforce-enrichment.md](01-gmail-websearch-salesforce-enrichment.md) | Gmail → web → Salesforce enrichment |
| 02 | [02-elon-xpost-crypto-dashboard.md](02-elon-xpost-crypto-dashboard.md) | X post → 6-coin crypto dashboard |
| 03 | [03-what-did-i-do-last-week.md](03-what-did-i-do-last-week.md) | Weekly recall from journals |
| 04 | [04-why-did-you-do-it-this-way.md](04-why-did-you-do-it-this-way.md) | Instruction memory / “why” |
| 05 | [05-install-csharp-behavior-script.md](05-install-csharp-behavior-script.md) | C# behavior install on synapses |
| 06 | [06-rich-chat-image-sales-chart.md](06-rich-chat-image-sales-chart.md) | Rich chat: image in, chart out |
| 07 | [07-multitool-turn-approval-gate.md](07-multitool-turn-approval-gate.md) | Multi-tool + human approval |
| 08 | [08-calendar-conflict-email-send.md](08-calendar-conflict-email-send.md) | Calendar conflict + email |
| 09 | [09-cross-module-correlation-one-thread.md](09-cross-module-correlation-one-thread.md) | One causal thread across modules |
| 10 | [10-live-dashboard-stream-subscription.md](10-live-dashboard-stream-subscription.md) | Live dashboard subscriptions |
| 11 | [11-voice-note-tasks-calendar.md](11-voice-note-tasks-calendar.md) | Voice → tasks + calendar |
| 12 | [12-mcp-tools-ide-federation.md](12-mcp-tools-ide-federation.md) | MCP / IDE tool federation |
| 13 | [13-multidevice-session-handoff.md](13-multidevice-session-handoff.md) | Multi-device session handoff |
| 14 | [14-compliance-legal-hold.md](14-compliance-legal-hold.md) | Legal hold / compliance |
| 15 | [15-travel-booking-multi-approval.md](15-travel-booking-multi-approval.md) | Travel multi-approval |
| 16 | [16-invoice-ocr-accounting-payment.md](16-invoice-ocr-accounting-payment.md) | Invoice OCR → books → pay |
| 17 | [17-team-standup-synthesis.md](17-team-standup-synthesis.md) | Team standup synthesis |
| 18 | [18-opportunity-close-gmail-sequence.md](18-opportunity-close-gmail-sequence.md) | Deal-close email sequence |
| 19 | [19-shell-widget-behavior-live-author.md](19-shell-widget-behavior-live-author.md) | Live widget + behavior authoring |
| 20 | [20-web-research-brief-citations.md](20-web-research-brief-citations.md) | Research brief + citations |
| 21 | [21-meeting-transcript-action-fanout.md](21-meeting-transcript-action-fanout.md) | Meeting → action fan-out |
| 22 | [22-crypto-wallet-tax-journal.md](22-crypto-wallet-tax-journal.md) | Wallet tax journal |
| 23 | [23-customer-churn-alert-cascade.md](23-customer-churn-alert-cascade.md) | Churn alert cascade |
| 24 | [24-behavior-hot-reload-inflight-asks.md](24-behavior-hot-reload-inflight-asks.md) | Hot-reload under open asks |
| 25 | [25-owner-isolation-shared-silo.md](25-owner-isolation-shared-silo.md) | Isolation (deployment model) |
| 26 | [26-behavior-hot-reload-live.md](26-behavior-hot-reload-live.md) | Hot-reload while live traffic |
| 27 | [27-multi-owner-isolation.md](27-multi-owner-isolation.md) | Multi-owner isolation |
| 28 | [28-implicit-stream-wake.md](28-implicit-stream-wake.md) | Implicit stream wake (ingress) |
| 29 | [29-long-running-research-progressive-ui.md](29-long-running-research-progressive-ui.md) | Long research + progressive UI |
| 30 | [30-midstream-correct-cancel-replan.md](30-midstream-correct-cancel-replan.md) | Cancel / replan mid-stream |
| 31 | [31-crypto-stoploss-social-signal.md](31-crypto-stoploss-social-signal.md) | Social signal → stop-loss |
| 32 | [32-meeting-notes-tasks-slack.md](32-meeting-notes-tasks-slack.md) | Notes → tasks → Slack |
| 33 | [33-whiteboard-photo-tasks.md](33-whiteboard-photo-tasks.md) | Whiteboard photo → tasks |
| 34 | [34-replay-last-tuesday-journal.md](34-replay-last-tuesday-journal.md) | Time-travel journal replay |
| 35 | [35-self-heal-delivery-failed.md](35-self-heal-delivery-failed.md) | DeliveryFailed self-heal |
| 36 | [36-script-react-all-email.md](36-script-react-all-email.md) | Script reacts to all email |
| 37 | [37-nested-asks-memory-vector.md](37-nested-asks-memory-vector.md) | Nested asks (chat→memory→vector) |
| 38 | [38-rich-multimodal-assistant-response.md](38-rich-multimodal-assistant-response.md) | Chart + image + buttons |
| 39 | [39-nightly-batch-gmail-calendar-crm.md](39-nightly-batch-gmail-calendar-crm.md) | Nightly multi-system batch |
| 40 | [40-voice-transcript-crm-email.md](40-voice-transcript-crm-email.md) | Voice → CRM + email draft |
| 41 | [41-oauth-refresh-mid-workflow.md](41-oauth-refresh-mid-workflow.md) | OAuth refresh mid-workflow |
| 42 | [42-share-pane-not-journals.md](42-share-pane-not-journals.md) | Share UI pane, not journals |
| 43 | [43-adversarial-prompt-injection-email.md](43-adversarial-prompt-injection-email.md) | Prompt injection via email |
| 44 | [44-rolling-module-grain-version.md](44-rolling-module-grain-version.md) | Rolling module grain version |
| 45 | [45-stateless-worker-embeddings-10k.md](45-stateless-worker-embeddings-10k.md) | Stateless worker embeddings |
| 46 | [46-reminder-wakes-dormant-30d.md](46-reminder-wakes-dormant-30d.md) | Reminder wakes dormant neuron |
| 47 | [47-pubsub-many-dashboards.md](47-pubsub-many-dashboards.md) | Many dashboards, one fact kind |
| 48 | [48-why-sales-dropped-multichart.md](48-why-sales-dropped-multichart.md) | “Why sales dropped” multi-chart |
| 49 | [49-marketplace-install-handlers.md](49-marketplace-install-handlers.md) | Marketplace N+1 handlers |
| 50 | [50-day-in-life-morning-brief.md](50-day-in-life-morning-brief.md) | Full morning brief day-in-life |

## Core claims these scenarios collectively force

| Claim | Forced by |
|---|---|
| Thin ABI: only `Synapse`, `INeuron<T>`, `NeuronId`, `SynapseMetadata` | All — modules speak facts, not frameworks |
| Journal is causal truth | 03, 04, 09, 34, 48 |
| Broadcast = Emit resolution, not a second bus | 02, 10, 47 |
| Streams = ingress/edge, not n2n truth | 10, 28 |
| Behaviors are neurons | 05, 19, 24, 26, 36, 49 |
| No neuron-awaits-neuron | 07, 30, 37 |
| DeliveryFailed is first-class | 35 |
| Reminders wake dormant work | 39, 46 |
| Rich multimodal is module UI facts | 06, 33, 38 |
| Catalog / N+1 install | 05, 44, 49 |
| Isolation is deployment (or explicit product policy) | 25, 27, 42 |
