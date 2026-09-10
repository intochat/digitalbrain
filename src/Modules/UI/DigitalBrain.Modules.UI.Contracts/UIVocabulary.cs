namespace DigitalBrain.UI;

// The UI module's vocabulary: its grain type and its signal type names. This project ships no
// C# signal types - a signal is a type name plus a JSON body - so the comment above each name
// is the documented contract for that body, and the records it points at are the shapes.
public static class UIVocabulary
{
    // ---- grain types ----

    public const string ChatType = "uichat";

    // ---- what other neurons say to the chat, and what it says back ----

    // { "commandId": "...", "text": "...", "context": [{ "path": "...", "schemaHash": "...", "payloadJson": null, "blobRef": null }] }
    // The turn is named by this signal's own id - the work id the send returned - so it is not in the body.
    public const string TurnRequested = "TurnRequested";

    // { ...Responded... }
    public const string Responded = "Responded";

    // { "turn": "...", "commandId": "...", "detail": "..." }
    public const string TurnFailed = "TurnFailed";

    // The four kit signals share the body { "name": "...", "title": "..." } and become a card on the running turn.
    public const string ChartRendered = "ChartRendered";

    public const string GraphRendered = "GraphRendered";

    public const string ImageDescribed = "ImageDescribed";

    public const string SheetChanged = "SheetChanged";

    // ---- work a command schedules for its own reaction ----
    // This never travels along a synapse: a command validates and schedules, and the reaction
    // that drains the entry is where the snapshot is written and the outward signal is fired.

    // { "turn": "..." }
    public const string TurnCancelling = "TurnCancelling";
}
