# Shell neuron composition: source findings (2026-09-28)

## Recommendation

Use a hybrid shell: a small Flutter host owns device interaction, focus, navigation, window positioning, authentication/bootstrap errors and rendering. C# neurons own app data, app commands and declarative app content. First remove lifecycle and app-dispatch responsibilities from `workspace_app.dart`; then reuse UI-kit widgets for shell presentation. Moving every widget to a persistent C# grain would move complexity across the network instead of removing it.

The inspected root is 1,372 lines. UI composition alone does not remove its request lifecycle, save/recovery, controller ownership, remote window synchronization or artifact routing.

## What already exists

All source paths below are repository-relative.

- `src/Modules/Google/Flutter/Contracts/Surface/ISurface.cs`: a surface is a title and ordered named child references, with a revision.
- `.../Contracts/Layout/ILayout.cs` and `.../Flutter/Layout/LayoutNeuron.cs`: rows, columns, splits and stacks; gap; fixed child extents or flexible remainder. No breakpoint model or resize interaction contract.
- `.../Flutter/Workspace/WorkspaceNeuron.cs`: durable open/close window references, expected revisions, operation IDs and first-run suggestions. This is window ownership, not a complete shell renderer.
- `src/Applications/IntoChat/IntoChat/Apps/AppSurfaceComposer.cs`: real C# compositions already build Files toolbar/list/footer, image tabs/canvas, Leads collections and a Form surface.
- `.../Apps/BuiltIn/SettingsApp.cs`: real persistent preferences and C# textfields/button/layout/surface; applies display name and theme. It does not replace the current rich Settings UI (connections, UI Kit and other shell settings). Using it wholesale would drop existing functionality.
- `.../Apps/BuiltIn/AssistantApp.cs`: C# input/send/response surface, agent ownership and conversation lookup. Its simple surface is not parity with the full chat composer, transcript, microphone and client interaction.
- `src/Modules/Google/Flutter/app/shell/lib/workspace/apps/built_in_app_view.dart`: a generic built-in surface host already reads named nodes, sends events and reloads the app after actions.
- `.../workspace/app_surface_host.dart`: a more specialized Files/Image host; still contains file navigation and image edit/save dispatch in Dart. Existing C# surface composition does not itself eliminate that behavior.

## UI kit is two distinct layers

`src/Modules/Google/Flutter/app/ui/lib/digitalbrain_ui.dart` exports reusable native Flutter controls (chat, table, browser, card, chart, project dock, Lumen controls and others) as well as `NeuronView`.

`.../src/composition/renderer_registry.dart` lists 29 UI kinds, but only 10 have dedicated `NeuronView` renderers: surface, layout, collection, imagecanvas, button, text, textfield, card, tabs and form. The other 19 deliberately display a fallback. A standalone `UiDataTable` or `UiChart` existing in the Dart kit does not mean it is wired into generic C# composition.

`.../src/composition/neuron_view.dart` exposes load/action/activation callbacks and renders nested references. It currently renders basic Material text/buttons/fields directly; it does not automatically use every themed kit component. Text uses `Text`, not the kit's Markdown widget. Cards assign child height 240. Split is a horizontal Flex, not a user-resizable pane. Tabs are choice chips. These are workable app-content primitives, not an equivalent implementation of the current shell.

There is also a contract inconsistency to address before adoption: `.../Flutter/Shared/CompositionValidation.cs` allows nine child kinds and omits `form`, although the registry, endpoints and `AppSurfaceComposer.Form` support form rendering. Nesting a form through the existing layout/surface validators would be rejected. This is source inspection, not a newly executed failure test.

## Integration limits

- `src/Applications/IntoChat/IntoChat/Apps/AppEndpoints.cs` lines 143 onward: `/node` allows those 10 renderer kinds and validates workspace `/apps/` or `/images/` scope. `/event` accepts button, textfield, tabs, collection and form. There is no generic shell navigation/account-switch/dock/voice command contract.
- `NeuronView` loads every referenced node separately and reloads when its passed revision changes. Descendants receive the same revision. A broad refresh therefore fans out into reads; this is not a subscription/delta renderer.
- `AppSurfaceComposer` writes separately to persistent child neurons while composing; every node is not a free declarative value. Moving all static shell layout into this approach would introduce additional storage writes and network requests.
- Renderer recursion is limited to depth 16 and 128 children, with cycle protection. Multi-node construction is not an atomic whole-tree publish.
- `BuiltInAppView` dispatches and reopens after an event; `AppSurfaceHost` has additional app-specific routing. Consolidating these adapters around a typed app session is a useful reduction before introducing more server-authored views.

## Adoption slice

1. Keep the Flutter root as dependency composition and bootstrap. Extract session/controller lifecycle, navigation, artifact hosting and window synchronization into separately testable modules.
2. Promote stable visual shell primitives into `digitalbrain_ui`: shell scaffold, bottom dock/account menu, window frame and menu/section patterns. They receive immutable view state and callbacks; they do not access HTTP or persistence.
3. Standardize app windows on one surface adapter. Keep specialized chat, browser and image device bridges explicit. Reuse the existing collection immediately for browse/list screens.
4. Choose one bounded server-authored view (e.g. app detail/preferences) as a parity pilot. Repair contract coverage and make generic renderers use existing kit controls. Do not replace all settings with the current two-field SettingsApp.
5. Before making the shell itself server-authored, add a batched immutable view snapshot, typed action IDs, scoped authorization, versioned contracts, responsive layout semantics and a clear refresh strategy. Persist meaningful app state, not a separate changing grain per visual decoration.

This is a source audit; no runtime code changed and no tests were executed for this report.

A concrete blocker for arbitrary C# app windows: `app/shell/lib/workspace/workspace_store.dart:397` maps every incoming non-table window to a generic `app` artifact, then assigns Files only for `app-files` and Images for every other ID. It does not preserve the incoming surface kind/name as the generic renderer identity. Repair this reconciliation and round-trip persistence first; then pilot a restored arbitrary surface window before expanding server-authored composition. This can reduce hard-coded Files/Image branches without redesigning the whole shell.

## Options and root ownership

| Option | Scope | Tradeoff |
| --- | --- | --- |
| Flutter refactor | Extract session lifecycle, artifact hosting, navigation/dialogs and responsive shell presentation; reuse native UI kit | Lowest migration risk; root becomes small, but moving code alone does not reduce total behavior or enable new C# composition |
| Hybrid (recommended) | Flutter refactor plus generic C# surface windows and gradual renderer improvements | Reduces app-specific branches while retaining native interaction quality; requires preserving surface identity and clear action contracts |
| Entire shell composed in C# | Server defines shell layout, menus, pages and commands; Flutter renders the contract | Most flexibility but largest new protocol/renderer project; current kit lacks feature parity and efficient whole-view transport |

Suggested ownership: WorkspaceSession owns startup/recovery and remote-controller lifetime; ArtifactHost owns table hydration, revision reconciliation and specialized editor adapters; WorkspaceAssistantPane owns chat selection and transcript mounting; a pure UI-kit shell scaffold owns responsive placement; shell navigation owns dialogs and routes. The root wires these modules together. Avoid moving all logic into WorkspaceStore (already 897 lines) or an equally large new controller.

A reasonable design target is roughly 200–300 lines for workspace_app.dart after extraction, not a measured implementation result or a promise of fewer total lines. Acceptance should include restored arbitrary surfaces, preserved chat/voice behavior, table request cancellation, server-only startup, menu navigation and desktop/mobile layout tests—not only a file-size threshold.