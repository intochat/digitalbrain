# IntoCaht workspace

The production Flutter entry point now opens the project workspace designed in `design/workspace-options`. Projects contain conversations and saved work. Agents are selected beside the composer and do not own or replace the project.

## Interaction

- Opening a project goes directly to its workspace. The chat header switches between project conversations and creates new ones; saved work is available from the searchable Project files browser. Legacy project URLs also open the workspace.
- Chat uses a unified composer with attached context, a specialist menu, voice drafts and send/stop. Enter sends; Shift+Enter inserts a newline. User messages use a quiet surface, assistant responses use selectable Markdown, and tool details stay collapsed until opened.

- All production workspaces open the combined homepage: projects on the left and an Explore template shelf on the right, stacked on narrow screens. IntoChat is the home button; there are no Projects or Explore tabs. Templates offer New IntoChat project, Salesforce Admin, Lead Researcher and Automation Builder. Empty workspaces show the same homepage without creating a placeholder project. Legacy /explore links resolve to this homepage.
- Starting a project chooses an initial specialist, accepts a goal and an optional project name, and opens the goal as an unsent composer draft. The specialist and draft are saved with the conversation. Specialists remain changeable; choosing one does not connect a service or activate automation.
- Minimized editors appear in a compact dock with artifact-type icons and distinct titles. Restore retains the existing editor state and window bounds and does not attach the artifact to chat.

- Chat uses the shared `UiChat` / Flutter Chat UI component with streamed Markdown and compact tool receipts. Tables, images, Markdraw drawings and brain scenarios open on the right.
- Open shows an editor. Attach adds an explicit live reference to the conversation. New conversations start with no attachments. The picker supports multiple artifacts, and table context includes filters and selected row IDs.
- The first open item fills the workspace beside chat. Restore makes it a movable, resizable window; double-clicking its title maximizes or restores it. Dragging to the left, right or top edge previews snapping. Project files offers Open beside for comparison and Open as window.
- Window controls provide minimize, maximize/restore and close. Closing leaves the saved artifact in Project files. Window placement, floating bounds and minimized state persist per project. The bottom dock restores minimized items without losing editor state.
- Chat can collapse to give the workspace more room while preserving the conversation draft. Narrow workspaces show one active editor; phones switch between Conversation and Workspace.
- Settings is a full page with Profile, Appearance, Connections, Agents and Developer tools. The developer gallery renders real ui components including the table. Profile fields are device preferences; connection setup uses the existing service authorization flows.
- Voice records a draft, transcribes through `/agent/transcribe`, and waits for an explicit Send. Cancel, navigation and app lifecycle changes stop recording. Recordings are bounded to two minutes.

## Data and tools

Tables continue to be authoritative UI neurons. Human controls and agent tools share the same revision-checked service. CSV, TSV and the first worksheet of XLSX can be imported into those tables. Imports reject oversized data rather than truncate it. XLSX reads stored/cached values, not formula recalculation; date formatting is not applied to numeric Excel serials.

Diagram, brain and image documents are saved through `/workspace/artifacts`, with revision checks and atomic replacement in the single-owner host's `.digitalbrain/workspace` folder. Override `DigitalBrain:Workspace:StoragePath` to place these files on persistent storage. These document records are not represented as new typed neurons yet. UI layout and conversations are stored on the device, scoped by backend origin and non-secret owner identity.

The agent can call `create_artifact`, `read_artifact`, `update_artifact`, and `list_artifacts`, alongside the existing table and Tavily `search_web` tools. Drawing content contains Markdraw source. Image originals are retained alongside presentation edits. Brain scenarios use the same node/synapse visual model as the existing brain graph; generated scenarios are drafts and do not activate external integrations or execute work. The observed brain is available on demand through the read-only brain endpoints. Graph mutation/execution routes remain optional.

Client conversation history survives app restarts. The hosted AG-UI session store remains server memory: a kernel restart loses model session history. The client advances its continuation ID only after draining `RUN_FINISHED` to EOF.

Attached unsynced editor changes include their local draft and revision in model context. Live brain attachments contain dated observations, including stale/unavailable status, and use the existing snapshot event stream. Synthetic observation IDs are not editable document IDs. Image binary is excluded from text context; metadata and edit state remain available.

Native interaction evidence and verification limits are recorded in [workspace verification](design/workspace-verification/README.md).

## Dependencies

Markdraw 0.2.0 currently requires two narrow workspace overrides: `freezed_annotation 3.1.x` (Markdraw declares but does not use it; Chat Core needs 3.x) and `re_editor 0.9.0` (implements the current Flutter text-input interface while retaining the parameter names Markdraw uses). Real controller/editor tests cover the integration. Revisit these overrides when upgrading Markdraw.
