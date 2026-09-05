# DigitalBrain redesign: a visible assistant workspace

The deliverable is four interactive HTML design options and a current Flutter foundation comparison. The working Flutter app is preserved while the visual direction is selected. Run `node serve.cjs` from this directory, then open `http://127.0.0.1:8743`. The pages also work together as local files.

## Product decision

Make the activity graph the default home, with an always-available composer at the bottom. Ino occupies a clear place in the graph and a small presence beside the latest response. Keep the full transcript in an accessible drawer, with a message count and a way back to current work. Do not remove durable history simply to make the surface quiet.

Recommend **Lumen** for daily use, with Aurora's persona treatment and Atlas's inspector depth. Tactile explores restrained neumorphism without depending on an old neumorphic widget framework. All four directions use icon-based neurons and source-owned synapses.

## First launch and return visits

1. Restore the actual conversation and selected workspace. Show a calm Ino and a useful first prompt, without invented ongoing work.
2. Render the module/neuron topology and the age/status of its last successful snapshot. Connected tools, disconnected accounts, disabled subscriptions and unavailable services are distinct states.
3. Show the latest assistant response if available. An empty workspace offers three concrete actions, not a wall of onboarding cards.
4. Start an actual request from the bottom composer. Retain its accepted command identity, optimistic row and durable reconciliation from the current app. Make send failure visible and recover from the journal using the same identity.
5. Keep the user's camera and selection stable as events arrive. Do not recenter every time a neuron becomes active. Offer “Follow this request” separately from free exploration.

## Graph representation

- A **module** is a stable region. It groups actual neuron instances; it is not a neuron merely because it is a module.
- A **neuron** is a recognizable icon tile with a label. Provider identity supplies the icon (Gmail, Salesforce, Aspire); the instance name and role disambiguate multiple accounts or neurons of the same type.
- A **synapse** is a directed connection belonging to its source. Its inspector separates the connection from the **signal** that most recently crossed it.
- **Bound** means an explicit subscription. **Learned** means a reinforced handled direct-delivery route. Never present handler capability as an automatic broadcast subscription.
- At rest, routes are subtle. During a request, highlight its actual causal path and dim unrelated work. Keep concurrent requests distinguishable by selection and labels, not only by colors.
- A pulse means an observed delivery; its visual duration is explanatory, not a fabricated measure of transport latency. No observed event means no invented pulse.
- Use 2D/2.5D as the readable default. Optional 3D overview may help explore large modules, but text, icons, edge picking and keyboard navigation must remain usable. HTML studies are 2D/2.5D, not a new WebGL implementation.
- For large graphs, collapse quiet modules, cluster repeated instances and show counts. Avoid moving or animating every node continuously. A list/directory is the accessible alternative to spatial discovery.

## Inspectors

**Neuron:** name, instance, module, provider/account, availability, current activity, most recent observed event, useful current information, incoming/outgoing connections, and links to relevant artifacts or traces. Do not dump entire internal state by default. Do not expose credentials or claim internal model reasoning is an observable trace.

**Synapse:** source, target, signal contract, Bound/Learned kind, active state, creation/update information when available, last delivery time/status, correlation/causation identifiers under advanced details, a bounded payload preview, and recent delivery history. Payloads are sampled/limited; the UI must not imply the edge stores all historical data.

**Subscription controls:** unsubscribe removes the source-owned Bound edge. Mark the mutation pending until the system confirms it; on failure, retain/reconcile real state and display the error. After unsubscribe there is no learned remnant through which a broadcast can keep delivering. Direct sends retain their separate semantics. Resetting a visual playback is not a production subscription mutation.

## Ino's presence

The survey reference led to `D:/Projects/ino/clients/ino.flutter/assets/rive/persona_orb.riv`, `emoji.riv`, `lib/persona/persona_widget.dart`, and the April 16 Rive persona design. The existing README marks some fine-grained activity inputs as future wiring; do not assume the old assets already implement the full proposed state contract.

Use one authored Rive persona, driven from a small presentation state: idle, listening/composing, accepted, working, reading, searching, waiting for user, succeeded, failed, disconnected. Bind current action and energy from known workflow state. “Thinking” is a coarse running status, not a visualization of private chain of thought. Never generate a new animation asset per tool. Prefer ordinary Flutter transitions and pulses for the other neurons.

The HTML personas are CSS design stand-ins. The eventual Rive artwork should be reviewed for style, supported state-machine/data-binding contract and asset provenance before reuse. Lottie remains useful for authored one-shot decorative clips, not the central behavior model.

## Motion and attention

Keep idle movement subtle and localized to Ino. Active neurons get a restrained halo, a readable action label and a directional signal pulse. Finish with a short settled state, not endless celebration. Surface failure as a stable labeled state that can be inspected. Pausing the **visualization** must not silently cancel real work; cancellation needs its own explicit request semantics. Under reduced motion, use static active/complete markers and a text timeline. Never require animation or color alone to understand progress.

## Layout and accessibility

Desktop: the graph occupies most of the screen; composition stays at the bottom, context appears on selection, full history expands on demand. Mobile: a compact graph with pan/zoom, a searchable neuron directory, a bottom-sheet inspector and a keyboard-safe composer. Tooltips are supplementary. Every node and edge has an accessible name, visible focus, keyboard activation and an inspectable text equivalent. Support Escape, focus restoration, readable contrast and 44px control targets in the final Flutter components. Test high contrast, scaling and screen readers after choosing the kit.

Glass belongs on a few overlay surfaces such as the composer and inspector. Avoid a live backdrop blur behind every animated node. Neumorphic shadows communicate depth only; borders, contrast and labels communicate state. Use a solid fallback material on low-power devices and high-contrast mode.

## Flutter migration boundaries

1. Choose the visual direction and token set using these concepts.
2. Spike Forui inside `digitalbrain_ui_kit` for the composer, sheets, inspector tabs, dialogs, focus, input methods and desktop/web/mobile behavior. Compare `shadcn_ui` if Material/Flyer interoperability dominates the migration cost. The current SDK supports the researched versions.
3. Add public product components: `InoPresence`, `AssistantComposer`, `LatestReply`, `ConversationHistory`, `NeuronTile`, `ModuleRegion`, `SynapseInspector`, `ActivityTimeline`. Keep third-party component types private to the kit where practical.
4. Adapt the existing tested chat state to the compact presentation; preserve `chat-accepted`, exact command correlation, journal recovery, cancellation/disposal and visible errors.
5. Build a real graph projection with snapshot + sequenced updates, reconnect cursor, bounded retention/gap detection and current subscriptions. Existing journal rows supply traffic; illustrative HTML topology is not production topology. Never add an alternative runtime or store whose subscriptions can diverge from the source neurons.
6. Add a curated icon registry with fallbacks and account/instance labels. Reuse the existing Salesforce SVG where appropriate; use official provider assets with recorded provenance for production.
7. Connect typed subscription mutations and observed signal delivery; test Subscribe → Bound edge → broadcast delivery → Unsubscribe → no edge/no delivery.
8. Bind the authored Rive persona and introduce motion budgets. Verify real chat and graph behavior through Aspire/MCP plus native UI before retiring the old shell.

## What the HTML verifies

Visual hierarchy, the graph-first layout, icon selection, inspector structure, composer/history interaction, example routing, source-owned subscription semantics in local demo state, keyboard controls, pause/resume/reset and responsive behavior. It does not claim live topology, provider access, native Flutter performance, production Rive artwork, or runtime integration. The current Flutter dependencies and services are not changed by this design study.

See `research.html` for the sourced UI-kit matrix and `verification.json` for automated browser checks. The prototypes and preview images live in this directory.
