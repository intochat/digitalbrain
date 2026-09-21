# Applications composed of neurons

Proposed architecture, 21 September 2026. This refines `intochat-customer-product-design.md`. These are design contracts, not implemented APIs.

## Central decision

An app is a **versioned definition of a neuron composition and its behavior**. Opening it creates or resumes an app instance, whose root UI neuron is a **Surface**. Flutter renders that live surface. Assistant and the user operate the same instance through authorized commands.

A Card is a visual grouping inside an app or conversation. A Surface is the root of an app's interaction space. A window hosts a Surface; the Surface is not itself the desktop window and does not own minimize/maximize, screen coordinates, or Compute balance.

The model should output a structured app definition, not arbitrary Flutter source. A compiler validates it, resolves supported neuron types and operations, instantiates the composition, connects behavior, then activates a version. Built-in apps use this same system with curated definitions and registered specialized components.

## What is already present—and what is not

- `UiChildRef(Kind, Name)` already identifies child neurons.
- Card and Expander backend contracts already store child references; Tabs stores a `UiChildRef` per tab.
- `ButtonClicked(Name, Action)` and `TextFieldChanged(Name, Value)` already provide UI signals.
- `UiCardPart` and `UiCard` in the Flutter UI package currently represent title, body and fields, not recursive `UiChildRef` children. Their existence is **not** proof of end-to-end live composition. Build or adapt the recursive renderer explicitly.
- `UIVocabulary` uses kinds such as `card`, `image`, `button`, `textfield` and `tabs`. Keep the exact registered names; do not invent a parallel `ui.*` naming convention for all neuron types. The workspace's existing alias is separately `ui.workspace`.
- Remote workspace windows currently reference tables. Hosting a Surface needs a compatible extension to that contract and its Dart model.
- `WorkspacePrograms` exposes compile/validate/deploy/version/run behavior in the current shell. Reuse or extend its underlying runtime after auditing it, rather than introduce an unrelated second behavior engine.

## Surface neuron

The proposed `surface` neuron owns a stable, versioned UI composition root for an app instance:

- Identity, app-instance reference, title and accessibility description.
- Named regions such as toolbar, navigation, body, inspector and footer, each pointing to a child neuron. Only body is required. Regions are optional; empty regions take no space.
- Presentation constraints: preferred minimum size, supported display modes, responsive collapse policy and semantic style tokens. The shell decides actual geometry.
- A documented lifecycle and a versioned definition reference. Define mounting/visibility explicitly so opening or re-rendering a surface cannot accidentally trigger a paid operation.

Prefer declarative region configuration plus versioned updates. Do not make Surface own every descendant's values, a provider client, or a generic executable script. Children own their state. Bind only allowed configuration fields and validate an update before activation.

Conceptual operations: configure/update the composition with an expected revision, read state, and subscribe to composition changes. The public signatures, serializers and migration strategy should follow the module's conventions during implementation.

A Surface can be mounted in a workspace window, an app preview, or another supported host. A child Card can be referenced from Assistant without duplicating its domain state. Focus, hover and caret are host-local; artifact values and completed operations are shared.

## The missing primitives

| Proposed primitive | Responsibility | Why existing controls are insufficient | Priority |
|---|---|---|---|
| **Surface** | Named app regions and a stable root composition | Card imposes card semantics; no generic application root exists in the inspected contracts | Foundation |
| **Layout** | Ordered child references using row, column, grid, split or stack modes, spacing and constraints | A child list does not describe an application layout | Foundation |
| **Form** | Named fields, validation, draft/commit semantics and submit action | Independent TextField signals do not provide an atomic validated form submission | Foundation |
| **Collection** | Keyed repeated presentation, selection and paging over a data source | Static children cannot represent a growing file gallery, history or job list efficiently | Needed for Files and scalable catalogs |
| **DocumentEditor** | Editable document, selection and versioned patches | Text displays/sets markdown; it is not a complete customer editor | Notes |
| **ImageCanvas** | Asset display, zoom/pan, crop handles and before/after comparison | Image displays a URL; generic sliders/cards cannot supply canvas interaction | Image Studio |

Use existing Button, Toggle, TextField, Slider, Progress, InfoBar, Text, Image, Tree, Tabs, Table, Sheet and Card where they fit. Upgrade Card/Expander/Tabs child rendering through the same host rather than maintaining separate composition systems.

Do not add a neuron for every Flutter widget. Padding, alignment, spacing, breakpoint behavior, scrolling and semantic theme variants are validated layout properties. Hover, dragging feedback, animation frames, pan/zoom preview and caret movement are local interactions, not a stream of persisted server neurons.

Dialog/drawer/menu hosting can initially be a typed overlay request referencing a child neuron and a host-local dismiss/result lifecycle. Promote it to a durable neuron only if there is a real requirement for persistent cross-session interaction state. A credential prompt uses a dedicated secure flow; Form must not turn secrets into ordinary persisted values.

### Layout rules

Use a bounded vocabulary of layout modes and sizing rules: fixed/bounded extent, content-sized, flexible, min/max constraints, gap and alignment tokens. Split panes reference children with stable keys; user-selected ratios are view preferences. Responsive rules select known alternatives, such as inspector becomes a drawer below a breakpoint.

Layouts cannot read arbitrary device APIs or evaluate generated Dart. Reject invalid constraints, render cycles, missing references and unsupported child kinds. Cap composition depth and expanded node counts. A shared child is allowed where its semantics support multiple views; cycles in the render tree are not. Domain/workflow graphs can have different cycle rules and must not be confused with the UI tree.

### Form rules

Inputs edit a local draft or explicit draft state. Submit sends a validated field map, form revision and command identity. Distinguish user edits from programmatic state synchronization so loading a form does not accidentally execute its behavior. Live search may emit a debounced query; a bulk paid action must never be wired to every keystroke or slider tick.

Cross-field validation belongs to Form or a referenced validation capability. Client validation improves feedback; server validation remains authoritative. Do not place authorization decisions in a visibility rule or disabled button.

### Collection rules

Use stable item keys and a data-source/query reference. A template binds an item's permitted fields to registered UI components. Pagination and selection must work without creating a permanent remote neuron for every visual cell. Persist selected item IDs only when needed. Separate template identity from per-item domain state so reordering does not mix state between rows. Flutter virtualizes the visible presentation.

## Example: generated product-photo application

User: “Build an app where I choose product photos from Drive, remove their backgrounds, and review the results in a table.”

The app definition contains this composition (names are local aliases, not global tenant IDs):

```text
catalog-prep [app definition, version 1]
  root: main [surface]
    toolbar -> controls [layout: row]
      chooseFiles [button]
      removeBackgrounds [button]
      exportResults [button]
    navigation -> sourceFiles [tree or collection]
    body -> content [layout: split]
      preview [imagecanvas]
      results [table]
    inspector -> options [form]
      outputFormat [registered input]
      keepOriginal [toggle]
    footer -> operationStatus [layout: row]
      progress [progress]
      message [infobar]

  behavior:
    chooseFiles.clicked -> open authorized file picker
    filePicker.accepted -> set selected asset references
    sourceFiles.selectionChanged -> select preview asset
    removeBackgrounds.clicked -> validate options -> request batch operation
    operation.progressed -> update progress
    operation.itemCompleted -> append result reference -> update results
    results.selectionChanged -> select result in preview
    exportResults.clicked -> save selected output copies
```

The compiler checks whether each input type and external operation is available. An unsupported output-format control is resolved to a supported input or causes a clear validation failure; it is not assumed to exist because it is written in the draft.

The app does not implement background removal itself. It calls a typed media capability through the shared operation runtime. That runtime handles access, estimate/reservation, provider execution, durable output and settlement. Reusing that capability means a button click, Assistant request and scheduled run have consistent behavior and costs.

## Behavior composition

Each binding names a source event, optional bounded transformation/condition, and an allowlisted target action. Type-check the source payload, target arguments and outputs. Resolve references to the current app instance; do not let generated names access arbitrary neurons across workspaces or tenants.

Use the existing signal/behavior system where appropriate. Separate UI-intent commands from persisted state-change events. For example, a model updating a TextField should not necessarily fire the same paid action as the user submitting the Form. Define this semantic distinction explicitly in the runtime and contracts; the current generic change signal alone does not prove it exists.

A command envelope should carry operation identity, principal/workspace, app/version, target/document reference, expected input revision and authorization/budget context. Proposed execution lifecycle:

1. Validate and authorize command.
2. Resolve input references and pin their versions.
3. Estimate/reserve Compute when needed.
4. Execute or enqueue a durable job with idempotency protection.
5. Commit output references and publish correlated progress/result events.
6. Settle usage once and update the visible state.

Do not describe event delivery as exactly-once without proving it. Design consumers to tolerate duplicate and out-of-order events with event/operation identity and versions. Long jobs must survive closing the window. Cancellation is a command with a truthful acknowledged state; closing a Surface is not cancellation.

## From description to installed app

1. **Understand:** extract inputs, outputs, workflow, desired UI and external capabilities from the request. Ask only for consequential missing information.
2. **Resolve:** use a catalog describing each available neuron's state, operations, emitted signals, rendering support, limits and compatible types. Backend-only support is insufficient for a visible control.
3. **Draft:** produce a versioned app definition: UI composition, data models, behaviors, required capabilities, input/output schemas and display metadata. Use reusable layout templates and semantic tokens so generated apps have consistent quality.
4. **Validate:** check renderability, references, field bindings, event/action compatibility, cycles, resource bounds and permission requirements. Reject missing capabilities honestly.
5. **Preview:** instantiate an isolated draft with sample data and simulated external actions. Preview must not trigger real paid work, deploy schedules or mutate customer files unexpectedly.
6. **Install:** activate the validated definition for the selected workspace, establishing authorized capabilities. Give it a launcher entry. Instantiation assigns scoped neuron identities and instance state.
7. **Run:** mount the root Surface, load a consistent initial snapshot, subscribe to state and route user/Assistant actions through the same command path.
8. **Revise:** conversational changes create a new draft version. Validate and migrate deliberately; keep the installed version available until activation succeeds. Running jobs stay pinned to their original definition.

“Describe a behavior” can create a headless workflow with run/status surfaces supplied by the platform. “Describe an app” creates a persistent interactive Surface plus behavior. They share actions and execution infrastructure without forcing every automation to have a custom dashboard.

## Definition, instance, document and window

Keep these identities separate:

- **Definition:** reusable versioned blueprint of neurons and behavior.
- **Instance:** a scoped runtime realization with child neuron references and app state.
- **Document:** durable user content that may be opened by different compatible applications.
- **Window:** local presentation of an instance/surface/document. Closing it releases subscriptions, not the document or job.

Do not share mutable state merely because two people installed the same app. Scope identities by tenant/workspace/instance and resolve symbolic definition aliases through that scope. Reopening the same document should resume or reuse the intended instance; “New document” explicitly creates another. A shared document can have multiple local views without sharing hover, caret or window bounds.

## Flutter host and update path

Introduce a host such as `NeuronView` (proposed name) that accepts a typed reference and context: identity resolution, state source, action dispatcher, theme and accessibility environment. A renderer registry selects the component. Surface and Layout recursively mount referenced children through this same host; Card/Tabs/Expander use it too.

Read a consistent snapshot with a revision watermark, then resume subscriptions from the appropriate position or reconcile on reconnect. Do not assume unrelated grain reads form an atomic application snapshot. Definition deployment should stage a validated composition and activate its root only when ready, so users do not see a partially constructed app.

Preserve widgets and controllers by stable child identity. A progress tick should rebuild the progress component, not reconstruct the image canvas and lose its zoom. De-duplicate shared subscriptions where appropriate, dispose them when the last view unmounts, and keep durable jobs independent of subscriptions. Render missing children or unavailable capabilities locally with retry/reconnect guidance.

Use semantic theme tokens, bounded typography and layout presets rather than model-generated colors and arbitrary pixel styles. A generated app should inherit the customer's chosen theme, text scaling and accessibility settings. Complex editor accessibility belongs to the registered editor renderer.

## Definition changes and lifecycle

Stage definitions and bindings before exposing the new version. Activation must not attach duplicate behavior listeners. Migration must specify which child identities/state are retained, transformed or retired. A UI-only label change should not reset a document. Removing a control must not silently abandon its outstanding operation or delete shared data.

Define retention and ownership separately from presentation. Dispose local views on close; retain persistent state according to app/document policy; explicitly uninstall a definition; explicitly stop schedules. Account for shared children before deleting anything. Failed migration leaves the old definition usable and surfaces a recoverable error.

## Implementation order

1. Add Surface and Layout contracts, state changes and serialization tests; extend workspace view references compatibly.
2. Build the recursive Flutter host and implement Surface/Layout rendering plus existing Card/Tabs/Expander child support. Prove a Surface containing a Card, input and button renders and updates correctly.
3. Wire one typed behavior: user submits an input, an existing capability runs, and another child updates. Verify the same action from Assistant and manual UI, reconnect behavior and revision conflicts.
4. Add Form and Collection with proper draft/submit and stable-key/pagination semantics. Define the app registry, validation and isolated draft preview over the existing programming runtime.
5. Add ImageCanvas and DocumentEditor alongside real media/document models. Build the five selected apps as curated compositions using shared capabilities.
6. Add custom-app generation/install/version migration using the same validated format. Do not ship an unvalidated “generate widgets and hope” path before the curated apps work.

The first architectural proof should be a small **working generated app**: a Surface with a form, a button, a result Card and a Table, whose behavior can be modified through the definition. It validates the platform before Image Studio's provider and billing complexity are attached.

## Acceptance checks

- A validated definition creates a visible app without a new handwritten app screen or Flutter rebuild.
- Nested child neurons update live; Card's backend children really appear in Flutter.
- User and Assistant invoke the same authorized behavior and observe the same document state.
- Resizing/rebuilding/reopening does not rerun actions or lose persistent values.
- Duplicate events/retried commands cannot duplicate paid work or charges.
- Unavailable kinds, cross-scope references, incompatible actions and render cycles fail validation.
- Editing a draft does not mutate the installed version; activation preserves compatible state.
- Large collections render lazily, and progress updates do not reset editor controllers.
- Changing window state never changes document ownership or a job's execution state.

## Evidence

Inspected `Contracts/Shared/UiChildRef.cs`, `Contracts/Card/ICard.cs`, `Flutter/Card/CardNeuron.cs`, `Contracts/Tabs/ITabs.cs`, `Contracts/Button/Signals/ButtonClicked.cs`, `Contracts/TextField/Signals/TextFieldChanged.cs`, `Contracts/UIVocabulary.cs`, `Contracts/Workspace/TableViewReference.cs`, `app/ui/lib/src/models/ui_part.dart`, `app/ui/lib/src/components/card/ui_card.dart`, and `app/shell/lib/workspace/workspace_programs.dart` under the Flutter module. This proposal relies on those local contracts rather than assuming a generic renderer or application runtime already exists.
