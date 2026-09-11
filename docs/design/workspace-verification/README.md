# Production workspace verification

Verified on 11 September 2026 against the native Windows Flutter app managed by the existing Aspire host. Screenshots below are from the running app, not the HTML prototype. Computer Use drove the mouse, keyboard, file pickers and microphone controls.

## Exercised with real services

- Sent a conversational request to search for Microsoft Agent Framework and create a sources table. The configured model called Tavily search and the table tool; three sources appeared in the working area.
- Applied a table filter in the UI, reducing three rows to one. Restarted the app and kernel, reopened the project, and recovered the filtered table and visible transcript.
- Explicitly attached that table. The agent read its authoritative state and identified the one visible row, then created a two-neuron research/review scenario.
- Rendered the generated scenario with the existing brain graph controls; opened the observed live brain separately.
- Attached the live brain and asked for its neuron count. The model reported five, matching a fresh backend snapshot, without trying to read the synthetic observation ID as a document.
- Compared table and brain side by side. Switched to floating windows, dragged, resized, minimized and restored editors.
- Imported the authored CSV fixture through the native file picker. Imported the repository's Flutter icon as an image fixture, rotated it, and confirmed the rotation after restart.
- Created a Markdraw document, drew a rectangle and reopened it after restart. Attached it alongside the table, switched to Salesforce Administrator without losing context, and asked the agent to read it. The agent described the saved rectangle and its dimensions.
- Opened full-page Settings and the actual UI components gallery. Tested project navigation and restored saved work.
- Started and cancelled actual microphone recording. Cancel returned to the idle microphone without sending a message or changing the draft.

## Repairs found through interaction

Native checks caught and verified fixes for missing graph theme scope, empty drawing creation payload, restored table hydration, floating-window drag accumulation and hit testing, hidden selected tabs, and a Windows recording-file deletion race. Live brain inspector selections remain local and do not attempt document saves.

Automated regressions additionally cover project/context isolation, EOF continuation, navigation during streaming, optimistic revisions, stale receipts, pending attachment edits, live-observation context, transcription failure cleanup, narrow layouts and CSV/XLSX parsing.

## Final checks

- `flutter test core/test ui/test shell/test`: 83 passed.
- `flutter analyze`: no issues.
- Focused .NET workspace, conversational-agent, table-agent, table-neuron and table-policy tests: 44 passed.
- Native Windows debug build and release web build succeeded. Web compilation reports the existing Cupertino font-family and WASM compatibility warnings; the JavaScript web target builds.
- The real `/chats/main/brain/events` endpoint returned `brain-snapshot` events. The workspace connects the existing graph watcher to that stream.

## Screenshots

| Evidence | Screenshot |
|---|---|
| Real web search and table | [Search table](01-search-table.png) |
| Filter restored after restart | [Restored filter](02-restored-filter.png) |
| Full-page settings | [Settings](03-settings.png) |
| Actual component gallery | [UI components](04-ui-gallery.png) |
| Generated brain scenario | [Brain](05-brain.png) |
| Side-by-side work | [Compare](06-compare.png) |
| Native CSV import | [Imported table](07-csv-import.png) |
| Rotated image and floating windows | [Image editor](08-floating-image.png) |
| Restored drawing | [Markdraw](09-markdraw.png) |
| Agent reads drawing state | [Drawing-aware answer](10-agent-drawing.png) |
| Agent understands the live brain | [Live observation](11-live-brain-context.png) |

## Scope and limits

The native microphone start/cancel path was exercised; speech recognition accuracy was not acoustically evaluated. Draft-only transcription, errors and lifecycle cancellation have automated coverage. Web compilation and route/widget tests do not constitute a live browser Back/Forward or mobile-device test.

The observed brain is read-only; generated scenarios are drafts, not running automations. Specialist choices configure conversation roles and available workspace tools, not installed external agents. Profile settings are local preferences. Existing integration authorization remains separate from the workspace UI.

Tables are UI neurons. Other artifacts are revisioned documents in the host's workspace store, not new typed neurons. Client transcripts persist, but kernel restart still loses the model's in-memory session. XLSX imports use cached values and do not calculate formulas or format Excel date serials. Image tools retain originals and edit presentation; text context does not send image pixels to the model.

See [production workspace](../../production-workspace.md) for configuration and dependency details.
