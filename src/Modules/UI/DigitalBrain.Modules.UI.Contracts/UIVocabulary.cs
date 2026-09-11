namespace DigitalBrain.UI;

// The UI module's vocabulary: its grain types and its signal type names. This project ships no
// C# signal types - a signal is a type name plus a JSON body - so the comment above each name
// is the documented contract for that body, and the records it points at are the shapes.
public static class UIVocabulary
{
    public const string TableType = "table";
    public const string TableCreating = "TableCreating";
    public const string TableUpdating = "TableUpdating";
    public const string TableListed = "TableListed";
    // ---- grain types ----

    public const string ChatType = "uichat";

    public const string SurfaceType = "surface";

    public const string ChartType = "chart";

    public const string GraphType = "graph";

    public const string ImageType = "image";

    public const string TranscriptType = "transcript";

    public const string WorkspacesType = "workspaces";

    public const string ActivitiesType = "activities";
    public const string ActivitiesInstance = "activities";

    // ---- what other neurons say to the UI, and what it says back ----

    // { "commandId": "...", "text": "...", "context": [{ "path": "...", "schemaHash": "...", "payloadJson": null, "blobRef": null }] }
    // The turn is named by this signal's own id - the work id the send returned - so it is not in the body.
    public const string TurnRequested = "TurnRequested";

    // { ...TurnAccepted... }
    public const string TurnAccepted = "TurnAccepted";

    // { ...KitCardOffer... }
    public const string CardOffered = "CardOffered";

    // { ...Responded... }
    public const string Responded = "Responded";

    // { "turn": "...", "commandId": "...", "detail": "..." }
    public const string TurnFailed = "TurnFailed";

    // The four kit signals share the body { "name": "...", "title": "..." } and become a card on the running turn.
    public const string ChartRendered = "ChartRendered";

    public const string GraphRendered = "GraphRendered";

    public const string ImageDescribed = "ImageDescribed";

    public const string SheetChanged = "SheetChanged";

    // { ...SurfaceOpened... }
    public const string SurfaceOpened = "SurfaceOpened";

    // { ...ComponentAdded... }
    public const string ComponentAdded = "ComponentAdded";

    // { ...ControlActivation... }
    public const string ControlActivated = "ControlActivated";

    // { ...ControlRefused... }
    public const string ControlRefused = "ControlRefused";

    // { ...ActivityExecutionChanged... }
    public const string ActivityExecutionChanged = "ActivityExecutionChanged";

    // { ...ActivityChanged... }
    public const string ActivityChanged = "ActivityChanged";

    // ---- work a command schedules for its own reaction ----
    // These never travel along a synapse: a command validates and schedules, and the reaction
    // that drains the entry is where the snapshot is written and the outward signal is fired.

    // { "turn": "..." }
    public const string TurnCancelling = "TurnCancelling";

    // { ...OpenSurface... }
    public const string SurfaceOpening = "SurfaceOpening";

    // { ...ActivateControl... }
    public const string SurfaceActivating = "SurfaceActivating";

    // { ...RenderChart... }
    public const string ChartRendering = "ChartRendering";

    // { ...AppendChartPoint... }
    public const string ChartAppending = "ChartAppending";

    // { ...RenderGraph... }
    public const string GraphRendering = "GraphRendering";

    // { ...DescribeImage... }
    public const string ImageDescribing = "ImageDescribing";

    // { ...AppendTranscript... }
    public const string TranscriptAppending = "TranscriptAppending";

    // { ...WorkspaceRecord... }
    public const string WorkspaceEnsuring = "WorkspaceEnsuring";
}
