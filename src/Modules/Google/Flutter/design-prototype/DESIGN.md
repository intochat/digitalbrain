# Current direction — three floating islands (revision 5)

This supersedes the single command dock below. Keep all controls on one selected edge, but split them into independent groups: workspace at the left, Assistant/open apps/Applications/Search in the center, and visible Compute balance/Settings/profile at the right. Bottom is the default; the existing Top setting moves all three together. The center dock can grow without moving the two outer groups. Workspace and account menus anchor to their corresponding groups. App launch and restore behavior is unchanged.

Options considered: (1) edge-aligned islands, recommended for stable targets and a spacious desktop; (2) three closely grouped center islands, easier to reach but visually crowded as apps accumulate; (3) a centered app dock with top-corner system islands, familiar but consumes both edges and conflicts with the goal of recovering workspace height. On narrow screens labels collapse and app icons scroll; Compute remains visible.

Flutter: use a shared dockPosition preference and independently laid-out shell groups. Balance remains server-authoritative. Existing neurons continue to own app content, while window state remains in WorkspacePresentation.

---

# IntoChat OS: unified command dock

Current direction: revision 4. This supersedes the separate top islands and bottom dock. HTML prototype only; production Flutter is unchanged.

## Shell recommendation

Use one floating command dock, bottom-center by default and optionally top-center. Group IntoChat/workspace, Assistant toggle, open application windows, Applications, Search, and profile. Icons have accessible names and hover/focus labels. Keep workspace identity readable on desktop. Settings, Compute balance and background activity live inside the profile menu. Files remains available from Applications. Remove the separate header, workspace count, Arrange and Files strip entirely.

A single dock saves a whole row of chrome and gives every global action one consistent home. Prefer this over separate top and bottom controls or a full-width bar. Retain a small safe area around the dock so maximized apps and Assistant input never sit underneath it. Menus open above a bottom dock and below a top dock. Do not auto-hide the dock in this iteration: stable targets are easier to learn. The layout can later collapse window groups if many documents are open.

Choose position in Profile → Workspace preferences → Dock position. The prototype remembers it for the current session only. Production should store a `dockPosition` preference alongside assistant position and appearance, with a bottom default and a safe fallback for older saved preferences.

Application launch behavior is retained: a newly launched app fills the work area and minimizes other windows; restoring its size records a floating preference; later launches and dock restores respect that choice. Menus are popovers, not fullscreen applications. Minimizing/closing an app does not cancel its background jobs.

The dock is shell presentation. Existing neuron content, IDs, typed actions and revision handling remain separate. Extend WorkspacePreferences for dock placement and continue using WorkspacePresentation for window state. App compositions still resolve existing Card, Tabs, Text, Tree, Button and Table neurons as described below.

Browser verification: removed topbar and workspace-strip confirmed absent; selecting Top placed the dock at the top safe margin; Bottom restored its default position; inspected browser error logs were empty. JavaScript syntax validation passed.

## Mapping to the actual Flutter module

Paths below are relative to `src/Modules/Flutter/`.

| Design surface | Existing implementation inspected | Proposed work |
|---|---|---|
| Spatial desktop | `app/shell/lib/workspace/workspace_desktop.dart`, `WorkspaceDesktop`, `workspaceWindowRect` | Restyle chrome and introduce the dock; retain editor identity, bounds clamping, existing snap/maximize behavior |
| Window state | `app/shell/lib/workspace/workspace_store.dart`, `WorkspacePresentation` | Reuse `openArtifactIds`, `minimizedArtifactIds`, `activeArtifactId`, `windowBounds`, `windowModes`, `restoreModes`, `chatWidth`, `chatCollapsed`; add chat-side preference with a left default |
| Appearance settings | `app/shell/lib/workspace/workspace_settings.dart`, `WorkspaceSettings` | Add assistant position and a chosen visual theme; reuse existing compact-density and reduced-motion preferences |
| Shared theme | `app/ui/lib/src/theme/ui_theme.dart`, `UiPalette`, `UiType` | Put final tokens here; the existing file explicitly directs shell and UI to share tokens |
| Window lifecycle neuron | `Flutter/Workspace/WorkspaceNeuron.cs`, `Contracts/Workspace/IWorkspace.cs` | Reuse `Open`, `Close`, `Read`, revision checking and operation receipts; do not send every drag/minimize as a backend close |
| Backend snapshot adapter | `app/core/lib/src/models/workspace_models.dart` | Current `WorkspaceWindow.fromJson` exposes `tableId` from `view.id`; generalize the view contract deliberately before claiming arbitrary neuron windows work through this route |
| Inline composite cards | `Flutter/Card/CardNeuron.cs`, `Contracts/Card/ICard.cs`, `app/ui/lib/src/components/card/ui_card.dart` | `CardState.Children` already accepts `UiChildRef`; render the same referenced content in chat or a window through the existing UI renderer rather than duplicate state |
| Table / notes / browser / charts | Existing `Flutter/Table`, `Flutter/Text`, `Flutter/WebBrowser`, `Flutter/Chart` neurons | Use typed capabilities to pick surfaces and allowed actions; note editing needs an editor contract, not merely static TextNeuron display |
| Secret input | `Flutter/TextField/TextFieldNeuron.cs` | Password kind exists, but requires a separate credential path; see below |
| Compute | No Compute backend is wired in this mockup | Add server-authoritative metering, estimates, balance and purchase flows as a separate subsystem |

Proposed flow: assistant requests a typed UI capability → an existing neuron exposes state and supported actions → the shell renders its registered Flutter component → user interactions call the supported operation → versioned updates refresh both card and window. The assistant gets capability results and references, not arbitrary access to widget internals. Bind artifact ID to neuron reference and conversation context explicitly. Treat this as a proposed composition layer, not a claim that all of it already exists.

## Credential boundary

The inspected `TextFieldNeuron` supports `kind = password`, but `SetValue` places the value in persistent state and emits `TextFieldChanged(name, value)`. A masked field is not a secure transport or storage boundary. Do not route real credentials through this ordinary neuron path.

Introduce a credential request containing a request ID, service, purpose, requested scope and expiration. Flutter submits the secret directly to a dedicated credential service over the authenticated channel. That service returns an opaque connection reference; chat, neuron events and the model receive only success/failure and that reference. Exclude secret values from transcripts, generic event streams, local preferences and logs. Clear the field on completion and cancellation. Use appropriate authorization and secure secret storage on the backend.

The HTML accepts a demo value only, clears it immediately, and performs no network submission or persistence. It deliberately does not demonstrate real authentication.

Flutter's `obscureText` masks displayed characters and prevents copy/cut, but does not by itself secure storage or transmission: [official TextField documentation](https://api.flutter.dev/flutter/material/TextField/obscureText.html).

## Compute experience

Use **Compute** consistently as the visible unit. The top bar shows available balance; opening it reveals usage by task. Before a metered action, show estimated usage and an upper limit when known. Afterward, show actual usage. Background automations need per-run limits and monthly limits. Pause or ask before exceeding the approved limit. Failed/retried work needs a defined charging policy.

Amounts in this prototype are illustrative. Pricing, metering, reservations, refunds, checkout, and authentication are not implemented. Never decrement a local UI balance as the billing source of truth. Server-side metering must prevent duplicate charges on retried operations.

## Flutter panel implementation

Clip the panel to its own boundary, then apply `BackdropFilter` and a translucent fill. Do not blur the full desktop for a small panel. Flutter documents that the filter operates within its ancestor clip and can be expensive. Overlapping filters should not share a backdrop key: [official BackdropFilter documentation](https://api.flutter.dev/flutter/widgets/BackdropFilter-class.html). Provide a solid-surface fallback for reduced transparency or constrained devices. Context7 was attempted but reported its monthly quota exceeded; these implementation notes were verified against official Flutter documentation instead.

## Run and inspect

From the repository root:

```powershell
python -m http.server 4318 --bind 127.0.0.1 --directory src/Modules/Flutter/design-prototype
```

Open `http://127.0.0.1:4318`. No build or package installation is required. State is in memory; refresh resets the demo while the selected design remains in the URL. `index.html` can also be opened directly with its adjacent CSS, JavaScript and icon file.

Try: move chat; minimize Notes and restore it from the dock; maximize the table; filter the shortlist; open all 12 sample rows; export CSV; send “automate this weekly”; connect using a made-up token; inspect Compute; search Applications; close and reopen a window; hide Assistant; show the desktop; switch appearance in Settings.

## Delivery scope

This is a reviewable design artifact, not production implementation. Keep the three directions available until a direction is selected. Implement the selected design with proper Flutter focus handling, semantic labels, contrast and text-scale checks, resizing and snapping, persisted preferences, live neuron updates, reconnect behavior, and secure input tests. The existing Flutter app and projects remain untouched.

## Neuron composition contract for this revision

The product exposes applications, documents/windows, and jobs as three separate concepts. A neuron is the underlying typed state/action unit. Do not surface each Button, TextField or Toggle as a standalone application.

| Product application / surface | Existing neuron composition | Boundary |
|---|---|---|
| Tables | `TableNeuron` / `ITable` | Data and table operations remain neuron-owned |
| Notes | `TextNeuron` / `IText.Set(markdown)` | Existing markdown content can be displayed and updated through supported actions; no unverified rich-text editor contract |
| Files | `TreeNeuron` / `ITree.Set(nodes)` and `Select(id)` | Tree nodes provide navigation; selecting a file resolves its artifact reference and opens a window |
| Automations setup | `CardNeuron` children containing `Text`, `Button`, and optionally `Toggle` | UI composition exists; scheduling execution is a separate capability, not provided by CardNeuron |
| Assistant result cards | `CardNeuron`, `UiChildRef(Kind, Name)` | Chat and window resolve the same reference and versioned state |
| Tabbed application | `TabsNeuron`, `TabItem(Id, Title, UiChildRef)` | Existing contract supports named child content |
| Settings | Card/Text/Toggle/Button controls as supported | Shell owns local layout preferences; credentials remain outside ordinary text-field state |

An application registry is proposed, with app ID, title, icon, supported root neuron kinds, launch action and required capabilities. The HTML includes a small illustrative `appRegistry`, but it does not connect to Flutter. The shell resolves a registered root neuron into the existing Flutter renderer and hosts it in a stable window. Child changes update content without rebuilding the whole shell or changing window identity.

The current remote workspace contract is specifically `TableViewReference(Id)`. This is a real implementation gap: arbitrary app windows need an explicitly versioned general view reference, reusing `UiChildRef` semantics where appropriate, plus migration for existing table references. Do not pretend the current table-only contract already hosts any neuron.

Window placement/minimized state remains in `WorkspacePresentation`. Backend Open/Close retains operation IDs and expected revisions. Background job state has its own lifecycle and must not derive from the minimized/open flag. Assistant may request an app or supported neuron action; it should not arbitrarily mutate shell widgets or write secret values through generic neuron events.

## Previous revision verification

Verified in the browser: close removes a window from the taskbar; minimize exposes Restore; launching Notes from a filtered Applications menu reopens the existing artifact; hiding Assistant preserves windows; Show desktop minimizes and restores its set; Background activity reports idle independently of window state. All 12 company rows render by default. JavaScript syntax check passes and inspected browser error logs are empty. Desktop (1440px) and narrow (390px) layout widths were checked for page overflow. This is interaction and visual prototype verification, not a Flutter integration test or complete accessibility audit.

## Revision 3 verification

Browser checks confirmed that launching Automations maximizes its window and minimizes both other windows; Restore returns it to floating size; minimize/restore and relaunch retain the user's floating choice; other windows remain minimized. Settings renders in the top-right popover. JavaScript syntax check passes and the inspected browser error log is empty. Preferred app size is in-memory for this prototype; reload resets the scenario.
