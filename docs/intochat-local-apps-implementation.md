# Local apps implementation

The approved first release has Files and Image Editor. Automations / Dev Studio remains disabled and explicitly labelled **Not implemented**. Local operations do not deduct Compute; the island shows an unavailable balance until a real account balance is connected.

## Composition and ownership

- Flutter supplies persistent Surface, Layout, Collection and ImageCanvas neurons, plus existing Button, TextField, Text, Card and Tabs neurons. Layout supports fixed and flexible child extents. Card and Surface descendants use the shared NeuronView renderer with cycle/depth limits and isolated missing-child errors.
- Files composes a Surface → Layout with a toolbar (navigation, breadcrumbs, filter, sort, refresh), selectable Collection and status/paging children.
- Image Editor composes a Surface → Tabs → document Surface → ImageCanvas. The reusable ImageCanvas owns pan/zoom, drawing controls and crop interaction. IntoChat owns the document recipe, revisions, snapshots and save receipts.
- Collection activation/selection and ImageCanvas edit requests have typed neuron contracts. Scoped UI events and app HTTP endpoints invoke the same FileExplorer/ImageDocument methods available to other neuron callers. No generated Dart or arbitrary code execution is introduced.
- Dev Studio is a future consumer of these contracts, not an implemented app generator.

## Local file boundary

The IntoChat host exposes configured local roots. AppHost defaults to the current Windows user's Downloads outside tests; tests explicitly supply an isolated directory. This reads the filesystem of the host computer, not the computer of an arbitrary remote browser. Cloud storage and a remote local-files bridge are outside this milestone.

Protected entry handles are bound to workspace, root and relative path. Listing records source size and timestamps so stale entries fail with a refresh message. Reparse points are excluded/rejected; Windows directory leases and resolved-file checks protect the allowed path during reads and saves. Sources are immutable content-addressed snapshots. Document identity additionally includes source location, keeping equal images in different folders independent.

Sources: PNG/JPEG, 32 MiB encoded and 40 million pixels. EXIF orientation determines normalized document coordinates. Save copy emits PNG beside the original, with collision-safe naming, exclusive temporary creation, bounded PNG integrity validation, atomic publication and durable exact retry receipts. Edits and saves carry unique operation IDs and captured revisions. Pending operations are retained in workspace persistence; switching images cannot rebind an export to another document.

## UI behavior

Startup opens the last workspace or creates Personal. Three floating islands replace permanent top/bottom bars. The profile menu moves them to the top or bottom and moves Assistant left or right. Applications is a compact launcher. Opening an app maximizes it initially and minimizes the others; its restored floating size is remembered. Minimized app widgets remain alive, including ongoing saves.

Image edits are non-destructive. Pen strokes use source coordinates; crop changes the export region. Drag selection and numeric crop entry support Apply/Cancel. Undo/redo is retained per document during the session (100 commands); the committed recipe survives reload. Save copy refreshes Files. The original is never overwritten.

## Verification

The IntoChat E2E journey uses the real host, Flutter frontend, app neurons and a temporary Downloads directory. It draws, crops, exports an actual PNG, verifies dimensions and a stroke pixel, checks the original checksum, and finds the saved copy in Files. Local-file, revision, save-retry, malformed-image, EXIF and composition tests complement the journey. Existing workspace restoration remains covered.

For an explicit local acceptance run, `INTOCHAT_LOCAL_ACCEPTANCE_IMAGE` can point the journey at an existing PNG. It reads the original and creates a new edited copy beside it; it never overwrites or deletes the source. `INTOCHAT_LOCAL_ACCEPTANCE_SCREENSHOT` optionally records the editor screenshot. Normal CI does not set either variable and uses only temporary fixtures.

Verified on Windows, 2026-09-21: Flutter module 37/37, Flutter UI 12/12, shell 16/16; scoped file/save/image validation 7/7; real app-neuron and edit-reducer checks; the browser journey and existing workspace restoration. The supplied 3840 × 2160, 11,875,509-byte `Untitled.png` also passed the browser journey using the actual Downloads root. Its original checksum was unchanged; the acceptance output is a 60 × 40 test crop named `Untitled-edited.png`.

The independent implementation review identified filesystem, retry, orientation, async-document and nested-layout issues. These were repaired and exercised by focused regressions before handoff. The export operation lock was also verified with a failing test when the lock notification was removed, then passing after restoration.
