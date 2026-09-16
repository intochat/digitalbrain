# DigitalBrain

A personal assistant that composes durable behaviors from registered capabilities.

## Graph

**Neuron**
An addressable participant with its own state, incoming work, journals and outgoing connections. A neuron receives and emits signals.

**Signal**
One named message carrying a JSON payload. Identity, causation and correlation belong to its delivery envelope.

**Signal contract**
A signal name and the payload schema its sender promises and its recipient requires.

**Synapse**
A directed connection from one neuron to another for one signal type. A connection can have manual ownership and multiple behavior owners. It remains active while any owner needs it.

**Journal**
A bounded record of incoming signals, outgoing signals or command outcomes. Journal retention is distinct from pending work and durable state.

**Command**
A requested state change with a stable identity. Acceptance can schedule durable work; acceptance is not proof that the work finished.

## Composition

**Capability**
A registered operation available to composition, with configuration requirements, input/output contracts and an ownership policy.

**Behavior definition**
A saved intent expressed as named roles, capability configurations, signal contracts and connections. It contains data, not generated executable code.

**Behavior**
A durable owner of a configured network that fulfills an intent. It manages the network's lifecycle, distinguishes owned processors from shared resources, and preserves other owners when it stops.

**Role**
A neuron's place within one behavior. A role is distinct from the identity of a shared resource.

**Shared resource**
A neuron whose identity and state exist independently of a particular behavior, such as a Twitter account or a chart.

**Owned processor**
A neuron configured for one behavior run. Its configuration is immutable during that run. Filters, mappings, actions and decision neurons are owned processors.

**Filter**
A processor that emits its input only when a configured condition matches.

**Mapping**
A processor that constructs a new payload from declared input fields and constants.

**Action**
A processor that invokes a registered operation. Retries retain the same logical action identity.

**Decision neuron**
A processor that uses an AI model to produce a schema-validated structured decision. It does not invoke tools; declared downstream connections determine subsequent actions.

**Composing assistant**
The conversational assistant that discovers capabilities and creates, inspects, starts and stops behaviors on the user's behalf. It is distinct from decision neurons inside a running behavior.

**Uncertain action**
An action whose external outcome cannot be established safely. Its work is paused and surfaced for inspection instead of being blindly repeated.

**Recipe**
An existing instruction-driven composition neuron. Saved recipes remain usable; they are not silently converted into behavior definitions.

## Sources

**Post**
A publication with a provider identity, author and text. Receipt retries do not represent additional posts within the source's declared deduplication window.

**Receipt source**
A neuron that durably accepts integration input and emits domain signals. Availability of its local contract does not imply a live external provider connection.

## Telegram reminders

- **Telegram source** (`IBot`): durable private-chat receipts from an authenticated webhook. User identity and update identity belong to the provider adapter; behavior roles cannot replace them.
- **Reminder collection** (`IReminders`): owns independent Time timers, absolute deadlines and the lifecycle of one-off reminders. Accepted scheduling requests emit `ReminderDue` or `ReminderRejected`; routine refusal does not fault the behavior action. Recovery uses original admission time.
- **Notification** (`INotification`): durable bounded UI inbox with idempotent publish and dismiss commands. `UiNotificationCard` renders an entry as plain text. It does not imply an OS push or outgoing Telegram message.
- **Telegram Mini App**: same-origin Flutter UI over signed, user-scoped requests; no generic kernel authority or bot token reaches the browser. The default per-user saved behavior connects receipt, decision, scheduling, refusal and notification paths. See `docs/v2/TELEGRAM.md`.
