namespace DigitalBrain.Kernel;

internal static class HttpSurfacePaths
{
    public const string OpenScenePath = "/shells/{shellName}/scenes";
    public const string ShellEventsPath = "/shells/{shellName}/events";
    public const string ActivateControlPath = "/scenes/{sceneKey}/controls/{controlId}/activate";
    public const string StreamMessagePath = "/chats/{chatName}/messages/stream";
    public const string ChatEventsPath = "/chats/{chatName}/events";
    public const string BrainTopologyPath = "/brain/topology";
    public const string McpOAuthCallbackPath = "/oauth/callback";
    public const string AuthorizationEventsPath = "/authorizations/events";

    public const string EventStreamContentType = "text/event-stream";
    public const string CacheControlNoCache = "no-cache";
    public const string SceneOpenedEvent = "scene-opened";
    public const string ChatTurnEvent = "chat-turn";
    public const string ChatDeltaEvent = "chat-delta";
    public const string AuthorizationEvent = "authorization";
}
