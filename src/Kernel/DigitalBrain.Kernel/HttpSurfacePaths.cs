namespace DigitalBrain.Kernel;

internal static class HttpSurfacePaths
{
    public const string OwnerCommandsPath = "/owner/commands";
    public const string ChatEventsPath = "/chats/{chatName}/events";
    public const string SurfaceEventsPath = "/surfaces/{surfaceName}/events";
    public const string BrainTopologyPath = "/brain/topology";
    public const string GraphEventsPath = "/graph/events";

    public const string AuthBootstrapPath = "/auth/bootstrap";
    public const string AuthLoginPath = "/auth/login";
    public const string AuthLogoutPath = "/auth/logout";
    public const string AuthMePath = "/auth/me";
    public const string AuthUsersPath = "/auth/users";

    public const string EventStreamContentType = "text/event-stream";
    public const string CacheControlNoCache = "no-cache";
    public const string SurfaceOpenedEvent = "surface-opened";
    public const string ChatTurnEvent = "chat-turn";
    public const string GraphChangeEvent = "graph-change";
    public const string ChatDeltaEvent = "chat-delta";

    public const string KindChatSend = "chat.send";
    public const string KindChatCancelTurn = "chat.cancel-turn";
    public const string KindChatButton = "chat.button";
    public const string KindSurfaceOpen = "surface.open";

    // Multipart voice note → STT → same durable chat.send path.
    public const string ChatVoicePath = "/chats/{chatName}/voice";
}
