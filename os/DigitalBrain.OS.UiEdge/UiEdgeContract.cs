using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.OS.UiEdge;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "UI HTTP route and SSE event names are the single public contract for host, tests, and clients.")]
public static class UiEdgeContract
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

    public const string McpOAuthCallbackPath = "/oauth/callback";

    public const string AuthorizationEventsPath = "/authorizations/events";

    public const string AuthorizationEvent = "authorization";

    public const string BehaviorsPath = "/behaviors";

    public const string BehaviorPath = "/behaviors/{behaviorId}";

    public const string BehaviorProposePath = "/behaviors/{behaviorId}/propose";

    public const string BehaviorTestsPath = "/behaviors/{behaviorId}/tests";

    public const string BehaviorApprovePath = "/behaviors/{behaviorId}/approve";

    public const string BehaviorActivatePath = "/behaviors/{behaviorId}/activate";

    public const string BehaviorStopPath = "/behaviors/{behaviorId}/stop";

    public const string BehaviorStartPath = "/behaviors/{behaviorId}/start";

    public const string BehaviorRunOncePath = "/behaviors/{behaviorId}/run-once";

    public const string BehaviorRollbackPath = "/behaviors/{behaviorId}/rollback";

    public const string BehaviorBindingsPath = "/behaviors/{behaviorId}/bindings";

    public const string BehaviorBindingPath = "/behaviors/{behaviorId}/bindings/{bindingId}";

    public const string BehaviorEventsPath = "/behaviors/{behaviorId}/events";

    public const string BehaviorChangeProposePath = "/behaviors/{behaviorId}/change/propose";

    public const string BehaviorChangeApprovePath = "/behaviors/{behaviorId}/change/approve";

    public const string BehaviorEvent = "behavior";

    public const string BehaviorEditorSurfacePath = "/surfaces/behavior-editor";

    public const string BehaviorEditorSceneKey = "behavior-editor";

    public const string BehaviorEditorSceneTitle = "Behavior editor";

    public const string AccountEnrichmentBehaviorId = "com.digitalbrain.account-enrichment";
}
