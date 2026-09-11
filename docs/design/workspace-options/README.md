# IntoCaht workspace design study

Open [the interactive gallery](index.html), or serve this directory: `python -m http.server 51840 --bind 127.0.0.1 --directory docs/design/workspace-options` from the repository root. The five HTML entry points share local CSS, JavaScript and assets; no build or external network is required.

| Option | Product direction | Strength | Tradeoff |
| --- | --- | --- | --- |
| [01 Focus desk](01-focus-desk.html) | Calm chat + tabbed workspace | Best everyday foundation; explicit Compare mode | Only one editor visible normally |
| [02 Tiled studio](02-tiled-studio.html) | Dense, dark analysis studio | Comparing tables without overlapping windows | More visual density |
| [03 Window desk](03-window-desk.html) | Free windows and minimized shelf | Multiple artifact types together; drag, resize, focus, tidy | More layout management; easiest to clutter |
| [04 Project library](04-project-library.html) | Stable project and artifact navigation | Finding and resuming long-lived work | Navigation consumes editor width |
| [05 Quiet stage](05-quiet-stage.html) | Large visual editor and nearby-work shelf | Image work and concentrated editing | Comparisons are less discoverable |

## Recommendation

Use **Focus desk** as the default, with the **Quiet stage** editor proportions for images and **Tiled studio** as an explicit Compare layout. Offer the **Window desk** only as an optional advanced layout. Add project navigation when the amount of saved work warrants it, using the library drawer first.

Keep primary navigation small: Workspace, Saved work, Conversations; Activity, Connections and Settings remain reachable. The graph can become an optional artifact/editor later, rather than occupying the right half permanently. Remove introductory display copy once a real conversation is underway; it demonstrates the visual voice here but should not consume ongoing chat space.

## What the old UI contained

Inspected commit `a88ee317`, before conversational-agent commit `7a307f30`. It had KitChat, responsive navigation, conversation/graph splitting, integration login entries, a history overlay and Activity. Settings links were in the optional scripted-home branch, not a completed global preferences system. The windowing demo supported dragging, resizing, raising, minimizing, restoring, closing and Tidy. Its sample bodies were not persistent, arbitrary artifact editors.

The presentation can be recovered while retaining the simpler streaming agent. Do not recover the old turn/journal transport merely to recover its appearance. Exact source evidence and limitations: [historical inspection](../../research/2026-09-11-workspace-ui-history.md).

## Flutter Chat UI vs GPT Markdown

These packages serve different layers, so there is no useful winner between them for the whole chat.

| Concern | flutter_chat_ui / KitChat | gpt_markdown |
| --- | --- | --- |
| Conversation structure | Message list, controller integration, composer and custom builders | No conversation shell |
| Formatted content | Optional Flyer text and streaming renderers support Markdown | Dedicated Markdown widget with code, tables, links, images and math |
| Streaming | Flyer streaming message component | Host updates rendered content; not a conversation transport |
| Artifact references | Custom message builder can render compact reference cards | A Markdown table is still rendered text, not a stateful table editor |
| Visual control | Customize builders, theme, spacing and composer through KitChat | Customize the appearance inside a message |

**Recommendation:** restore KitChat (`flutter_chat_ui`) around AG-UI events. Start with the Flyer text/stream renderer; retain GptMarkdown inside a message builder if its math or content customizations are needed. Use typed UI-kit editors for persistent tables and images on the right. Settings and window management belong to the app shell, independent of both packages. Editorial message styling is my preferred minimal direction; Settings in the prototype also demonstrates soft bubbles.

Repository lockfile versions inspected: `flutter_chat_ui 2.11.1`, `gpt_markdown 1.2.1`; these are installed versions, not a claim about the newest release. Current documentation was checked with Context7 for `/flyerhq/flutter_chat_ui` and `/infinitix-llc/gpt_markdown`, and primary repositories: [Flutter Chat UI](https://github.com/flyerhq/flutter_chat_ui), [GPT Markdown](https://github.com/Infinitix-LLC/gpt_markdown). The HTML studies illustrate our proposed styling; they are not screenshots or runtime benchmarks of the Flutter packages.

## Interaction and state contract

- **Artifact identity:** a table neuron holds its typed dataset and durable view state, including filters/sort/visible columns. An image artifact retains the original and edit state. A chat card references that artifact ID; it does not create another copy.
- **Workspace layout:** separately remember open views, active view, layout mode, window bounds and minimized state. A single artifact may appear in a comparison without duplicating its data. Reopening a view restores its state.
- **Context:** show the artifacts and selected rows the agent is working with above the composer. Editor focus may suggest a target, but explicit multi-artifact context must remain understandable. The prototype resets context when opening one artifact; a production version should pin multi-artifact selections until explicitly removed.
- **Tool results:** stream prose in chat, show a compact tool/change receipt, and update the corresponding artifact editor. Errors belong beside the failed operation with retry and preserved edits. Keep the original receipt as a historical statement, even if the artifact changes later.
- **Minimize:** park the view on the shelf. **Close:** remove the view; retain its saved artifact. **Delete:** a separate action in Saved work. No deletion is implemented in this prototype.
- **Compare:** show two named tables; preserve independent filters. A later version can link filters, align rows by key and highlight differences. Any comparison summary must name its scope; this demo explicitly labels the total as before filters.
- **Responsive behavior:** desktop uses chat left/work right; phones switch Conversation/Workspace. Multiple windows stack on small screens. Editors retain their own scroll area.

## Prototype scope and trying it

All data is fictional and local. Try filtering with the Price control and Apply, sorting prices, selecting rows, comparing, minimizing/restoring, opening saved work, changing Settings, or switching all five concepts. In Window desk, drag title bars, resize from the bottom-right grip or use Tidy. Image crop and brightness/contrast are preview-only CSS adjustments; the source asset is preserved.

The chat is deterministic: try `filter under $40`, `compare tables`, `open image`, or `brighten image`. There is no model invocation, web search, file upload or live account connection in this design study. Settings, data view state and layout are stored per concept in browser localStorage. Reset demo resets only the selected concept. Production services and Flutter files were not modified for this study.

Assets: existing repository artwork from `src/Assets/nuget/icon.png`; official Material Icons glyphs from the installed Flutter SDK. UI palettes draw on the existing Lumen and kit theme colors.

## Verification

JavaScript syntax checked with Node. Visually inspected all five concepts in Chrome at 1440 × 960 and the Quiet stage mobile layout at 390 × 844. Browser interactions exercised explicit table filtering (5 of 7 products at $40), two-table comparison, image minimization/restoration and the demo image command; image brightness persisted at 125% after navigation to the standalone option. Mobile document had no horizontal overflow; navigation/shelf intentionally scroll. This is a design prototype, not production integration testing.


## Product expansion: specialists, voice and developer tools

All five concepts now use **IntoCaht** as the product and assistant name, matching the requested spelling. This rename is scoped to the HTML design study; internal production identifiers and Flutter sources remain unchanged.

Settings → Developer tools → UI kit gallery restores the intended discovery path. It includes HTML previews and a complete inventory of the 16 entries in the current Flutter gallery. Production should route to the existing `KitGalleryScreen`, retaining its actual widgets and normal/loading/error/disabled state controls; include the new `KitDataTable` there as well.

Agent marketplace offers IntoCaht, Salesforce Administrator, Lead Generator and Automation Agent. Each specialist has its own locally saved conversation, editor state and layout per concept. The header identifies the specialist, while IntoCaht remains the assistant identity. These are product previews, not installed or connected agents.

- Salesforce Administrator: sample Account-to-Contact/Opportunity relationships, inspectable objects, a Case addition, persisted notes and readable Markdraw source. Production should embed `MarkdrawEditor`, persist its serialized drawing in a UI neuron and let agent tools read/update that document. Markdraw supports controller-driven scene loading and serialization. [Markdraw documentation](https://pub.dev/packages/markdraw), checked via Context7.
- Lead Generator: fictional company table, industry filtering, row selection and contextual chat. Production lead collection needs provenance and freshness on each row; sample companies are not researched leads.
- Automation Agent: inspectable workflow graph, add-review-step control and review panel. The graph is a saved workspace artifact, opened or generated on demand rather than a permanent application pane. Workflow draft/revision state should be distinct from execution and activation state.
- Voice: a first-class composer action. Dictation produces an editable draft, never an automatic send. The HTML prototype implements browser SpeechRecognition when supported, with permission/error handling and Stop. Closing the dialog, hiding the page or switching workspaces stops capture. Browser recognition may use its speech service; the interface says so before starting. A clearly labeled sample transcript lets users explore without a microphone. The production Flutter app should restore its voice input path and cover all supported platforms. [SpeechRecognition documentation](https://developer.mozilla.org/en-US/docs/Web/API/SpeechRecognition).

Try “Create a workflow for qualifying new leads”, “show Salesforce diagram” or “filter leads to Software”. These commands open prepared local examples, not model-generated output. Real Markdraw editing, Salesforce metadata access, agent installation and workflow execution remain production integration work.

Verification of expansion: both scripts pass Node syntax checks. Chrome checks covered marketplace → Salesforce workspace, lead filtering to two Software companies, voice sample → draft → explicit send → workflow workspace, and Settings → developer gallery with all 16 catalog entries. No browser script errors were captured in those flows. Live microphone capture was not exercised; the sample path was verified without requesting microphone permission.


## Everyday workspace polish and brain correction

The latest pass supersedes the earlier linear workflow concept. The relevant baseline is `master` at `66440fbf`, specifically `LumenBrainGraph`: a pannable whiteboard of neurons with stable positions, movable nodes, inspectable synapses and delegation/activity presentation. The graph represents a connected scenario across AI agents, integrations, decisions and data. It is not a sequence of workflow cards.

The HTML brain preview now has nine typed nodes and eleven relationships. IntoCaht coordinates Salesforce Administrator, Lead Generator and Automation Agent; these connect to Salesforce, web research, lead data, a CRM diagram and human review. Selecting a node highlights its relationships and opens contextual details. Connections can be inspected independently. Saved data nodes can open their editor. Node positions persist per specialist workspace. Pan, zoom, fit, graph search and keyboard movement are available. This remains a prepared topology; live activity and real snapshot binding belong to the Flutter integration.

The polish keeps the product useful at rest:

- Remove the large introductory slogans, repeated specialist description card, repeated artifact headings, empty minimized shelf, and always-visible persistence/debug labels.
- Move concept selection and prototype explanations into Design study. All five layouts remain available through Workspace layout.
- Keep one compact application header, a navigation rail, chat and the working area. Desktop chat width is adjustable by pointer or keyboard.
- Use consistent Material icons, quiet borders, readable message text, tighter data rows and slightly stronger secondary text contrast.
- Keep voice, the composer and explicit artifact context visible. Clear context has a labeled icon action; hidden editors can remain intentionally in context.
- Show close/minimize/details under Editor actions for a focused editor. Retain window chrome for floating windows and comparison panes.
- Reveal graph relationship labels and details on selection; keep the canvas quiet otherwise. On phones, begin at readable node scale with pan/zoom and a node list, rather than shrinking every label to fit. Fit graph still shows the complete overview.
- Preserve reduced-motion preferences and keyboard focus outlines. Core graph and resize actions are labeled and keyboard-operable. This is not a claim of a full accessibility audit.

Visual checks covered the before/after desktop workspace at 1440×960 and the phone layout at 390×844. Browser checks covered node and relationship details, keyboard node movement (including persistence after reload), keyboard chat resizing, editor minimization/restoration and the mobile node list. The phone document had no horizontal overflow. JavaScript syntax checks passed and no script errors were captured during the checked interactions. Pointer drag logic is implemented but was not exercised in browser automation this pass.

Only the design study files changed. Production Flutter components, services and existing uncommitted table work were preserved.


## Navigation refinement: organize around work

The new default removes the permanent feature rail. A compact header has three labeled destinations: **Conversations**, **Saved work**, and **Explore**. Conversations and Saved work open temporary drawers; the editor stays in place behind them. Explore contains specialist discovery. Search remains in the header with its existing keyboard shortcut.

The active specialist is selected beside the composer, where users decide who they are asking. Switching still opens that specialist’s own workspace and preserves its state. The conversation header displays the work’s name rather than repeating product and specialist names. Account → Settings, Connections, Activity and developer UI-kit gallery gathers secondary destinations without filling navigation with features.

Outlined Material icons from the installed Flutter SDK distinguish navigation and artifact types. The conversation sits in a quiet inset panel, and the editor uses the width recovered from the rail. Mobile uses a compact two-row header and retains the Conversation/Workspace switch, without a permanent side menu.

The Conversations drawer currently lists the four sample specialist workspaces; it is not a production multi-conversation store. Saved work searches the local sample artifact catalog. A production implementation should use real conversation titles and scoped saved artifacts.

Comparison is available in **Design study → Navigation comparison → Switch navigation**, which toggles the feature rail and the new header arrangement. This compares navigation structure, not a frozen pixel-identical historical build.

Verification: Node syntax checks passed for the navigation script and icon map. Chrome checks covered Saved work search (a `leads` query produced Qualified leads), opening an artifact, switching specialists with the composer selector, account-menu access to all 16 catalog entries in the developer gallery, and mobile layout at 390×844 with no horizontal document overflow. The desktop arrangement was visually inspected at 1440×960. No script errors were captured during those checks. All changes remain in the HTML study.


## Project-centered model and full Settings

This iteration supersedes the earlier agent-owned workspace model. The recommended structure is **workspace → projects → conversations + work**. Agents and service connections are capabilities available across projects. An agent can be changed within a conversation without switching the project or replacing its references.

Two example projects are available: Revenue operations and Autumn collection. A project home brings its conversations and artifacts together. A conversation stores its own messages, selected agent and explicitly attached artifact IDs. Artifact data, filters and editor state are shared within the project, while open editors and window layout remain workspace presentation state. The prototype now persists these project states under a new local-storage namespace, leaving earlier concept data separate.

Opening and attaching are intentionally separate:

- **Open** shows the editor without changing conversation context.
- **Attach** adds a live reference to the current conversation, without forcing the editor to change.
- The composer attachment picker selects several project artifacts at once and shows table filters and row-selection summaries.
- Each context chip can open its artifact or remove its reference independently. A new conversation starts without attachments.
- Changing the agent preserves the project, conversation and attached references. Project switching restores its separate state.

Settings is now a routed full-page destination, opened by a settings icon. Sections are Profile, Appearance, Connections, Agents and Developer tools. The developer gallery opens inside Settings. Profile preferences and appearance can be saved locally; account authentication, billing and actual connections remain unimplemented. Appearance is a workspace-wide preference across the sample projects. Connection rows explicitly show that no services are connected.

Project-specific hash routes include the project ID, for example `#projects/revenue/workspace`, and Settings uses routes such as `#settings/profile`. This works when the HTML is opened directly from disk and lets browser navigation identify the project. Search is scoped to the current project's work.

Validation: scripts passed Node syntax checks. Desktop browser checks covered project overview, full-page Profile and saving profile preferences; selecting two attachments; opening the brain while preserving both references; changing the agent without changing the project/context; switching to the collection project with its distinct artifact list; and returning to Revenue operations with both references intact. Mobile Settings was inspected at 390×844 without horizontal document overflow. The checked flows produced no captured script errors. Account services and model/tool execution are still outside this HTML study.
