using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Flutter.Http;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "UI HTTP route and SSE event names are the single public contract for host, tests, and clients.")]
public static class FlutterHttpContract
{
    public const string HealthPath = "/health";

    public const string OpenScenePath = "/shells/{shellName}/scenes";

    public const string ShellEventsPath = "/shells/{shellName}/events";

    public const string ActivateControlPath = "/scenes/{sceneKey}/controls/{controlId}/activate";

    public const string AfterSequenceQuery = "afterSequence";

    public const string EventStreamContentType = "text/event-stream";

    public const string CacheControlNoCache = "no-cache";

    public const string SceneOpenedEvent = "scene-opened";

    public const string StreamMessagePath = "/chats/{chatName}/messages/stream";

    public const string ChatEventsPath = "/chats/{chatName}/events";

    public const string ChatTurnEvent = "chat-turn";

    public const string ChatDeltaEvent = "chat-delta";

    public const string BrainTopologyPath = "/brain/topology";

    public const string McpOAuthCallbackPath = "/oauth/mcp/callback";

    public const string AuthorizationEventsPath = "/authorizations/events";

    public const string AuthorizationEvent = "authorization";

    public const string BehaviorPath = "/behaviors/{behaviorId}";

    public const string BehaviorProposePath = "/behaviors/{behaviorId}/propose";

    public const string BehaviorTestsPath = "/behaviors/{behaviorId}/tests";

    public const string BehaviorApprovePath = "/behaviors/{behaviorId}/approve";

    public const string BehaviorEditorSurfacePath = "/surfaces/behavior-editor";

    public const string BehaviorEditorSceneKey = "behavior-editor";

    public const string BehaviorEditorSceneTitle = "Behavior editor";

    public const string AccountEnrichmentBehaviorId = "com.digitalbrain.account-enrichment";
}
