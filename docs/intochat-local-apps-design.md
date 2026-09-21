# IntoChat local apps: first usable release

Status: approved and implemented first-release scope, 2026-09-21. This narrows the earlier customer-product and neuron-composition documents for the next release; those documents remain longer-term directions.

## Product scope

Ship two working bundled apps: **Files** and **Image Editor**. Show **Automations / Dev Studio** in Applications with a disabled action and the explicit status **Not implemented**. Do not build an app generator, automation executor, Notes, or a new Tables app in this milestone. Preserve existing table functionality and saved workspaces.

Start in the last workspace, creating Personal workspace only when none exists. Remove the projects homepage from the startup/navigation flow. Keep workspace creation and switching in the workspace island. Retain three floating islands, positionable at the top or bottom: workspace on the left; Assistant, running apps, Applications and Search in the middle; Compute, settings and profile on the right. No permanent top bar, slogan, window count, Arrange action, or Files shortcut strip. Applications is a compact popover.

Opening an app initially maximizes it inside the work area and minimizes other app windows. Keep Assistant visible if enabled. Restore switches to floating mode; remember that choice per app and workspace. Selecting an already open app focuses its existing window. Opening an image activates Image Editor with that image; documents are tabs inside its one app window. Minimize hides presentation, without discarding editor state or cancelling a save. Controls need accessible names, keyboard access and icon tooltips.

## Files

The local IntoChat process reads configured roots on the same computer. Initially expose Downloads, configured for this installation as `C:\Users\vhorb\Downloads`. Never hardcode this user path in shared application code. E2E tests supply a temporary root. A cloud-hosted IntoChat process cannot provide this feature without a future local bridge.

Files supports folder navigation, breadcrumbs, refresh, filename filtering, name/date/size sorting, selection and metadata. Display unsupported files, but do not promise that they can be opened. PNG and JPEG open in Image Editor. No file deletion, moving, renaming, Google Drive, or arbitrary filesystem browsing in this release. Do not recursively index Downloads. Page directory results and avoid decoding images just to render the list.

## Image Editor

Day-zero tools: Fit, 100%, zoom, pan, freehand pen with color and width, rectangular crop with Apply/Cancel, undo/redo, Reset edits, and Save copy. Crop also has numeric X/Y/Width/Height fields. Exclude AI adjustments, background removal and upscaling. Keep the primary toolbar small; expose properties for the selected tool in a contextual inspector.

Edits are non-destructive. Snapshot the source bytes into application storage, persist the current editing recipe, and reconstruct the document on reopen. Source pixels use normalized orientation. Stroke coordinates and crop bounds use source-image coordinates, independent of viewport zoom and pan. Export clips the composed image and strokes to the crop rectangle. Undo/redo is session-local, capped at 100 commands; committed edits survive reload. Reset is an undoable command restoring the original snapshot. Crop changes the output region and does not rescale or delete stroke data.

Save copy produces an actual PNG next to the original, with a collision-safe name such as `Untitled-edited.png` or `Untitled-edited (2).png`. Never overwrite the original. Saving uses a captured document revision; subsequent edits remain unsaved. Retrying the same save operation must return the same saved file, including after process restart. Show progress, saved/unsaved state and actionable failures. After save, refresh Files and make the result selectable.

Proposed resource limits: source bytes at most 32 MiB, decoded source at most 40 million pixels, export at most 40 million pixels and 128 MiB encoded bytes. Check dimensions before expensive decoding, and show a useful rejection without losing existing edits. Verify these defaults against the supplied real PNG during implementation; do not silently downscale it.

## Neuron composition

The Flutter module owns reusable UI contracts, neuron implementations, renderers and interaction events. IntoChat owns application definitions, local filesystem access, file/document neurons and action orchestration. A Surface is a durable UI root containing references to children; it is not a filesystem API or a second business-logic layer.

Add Surface, Layout, Collection and ImageCanvas capabilities. Reuse Button, TextField, Card, Tabs and existing table types where appropriate. Collection provides a reusable selectable list/grid with item activation, metadata and paging; it does not know about Downloads. Layout has bounded row/column/split/stack composition. ImageCanvas presents a source asset and editing recipe and emits typed edit commands. The same child-rendering host must render Card children and Surface children.

IntoChat's Files app composes a Surface from toolbar, breadcrumbs, collection and status children. Image Editor composes document tabs, toolbar, canvas and inspector. Application controllers own state transitions and persist business state. Do not hardcode a whole Files screen into a generic neuron renderer. App UI events and future Assistant commands call the same app operations. No LLM or live provider is required for this milestone.

User-described app composition remains a future consumer of these contracts. Establish stable child identities, typed properties/events, permitted child kinds, revision checking, scoped bindings and missing-child/cycle handling now. Do not implement arbitrary generated Dart, expression evaluation, user code execution or a general workflow engine.

## Ownership and persistence

- Persist workspace/app references, document tabs, source assets and current editing recipes. Keep original file paths behind root-relative handles scoped to owner/workspace.
- Store binary sources and exports outside Orleans grain state. Grains store references, revisions, recipes and operation receipts.
- Stream authenticated bytes through IntoChat endpoints; do not use browser `file://` URLs or base64 blobs in persisted artifact JSON.
- Use current workspace scope and authentication for every app operation and asset request. Reject out-of-root paths, traversal, reparse-point escapes and unauthorized handles. Never treat a client path as authority.
- Save a copy using exclusive creation, bounded uploads, durable operation receipts and recovery after an interrupted write. Failed/incomplete output must not appear as a successful saved image.
- Local browsing and editing do not deduct Compute. Show the actual balance if available, otherwise an unavailable state; do not ship a made-up balance.

## Acceptance

On this installation, opening Files shows Downloads; activating `Untitled.png` opens `C:\Users\vhorb\Downloads\Untitled.png` in Image Editor. Drawing, cropping and zooming work together. Save copy creates a viewable edited PNG with correct pixels and dimensions; the original remains byte-identical. A real IntoChat browser E2E covers the same workflow against a temporary filesystem fixture, the actual Flutter frontend, application neurons and local file service. No mock UI or live AI provider substitutes for that path.
