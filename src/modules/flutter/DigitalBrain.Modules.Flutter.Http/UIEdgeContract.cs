using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.UI;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Product UI edge route and SSE names are the single public constant source for host, tests, and peers.")]
public static class UIEdgeContract
{
    public const string HealthPath = "/health";

    public const string OpenScenePath = "/shells/{shellName}/scenes";

    public const string ShellEventsPath = "/shells/{shellName}/events";

    public const string ActivateControlPath = "/scenes/{sceneKey}/controls/{controlId}/activate";

    public const string AfterSequenceQuery = "afterSequence";

    public const string EventStreamContentType = "text/event-stream";

    public const string CacheControlNoCache = "no-cache";

    public const string SceneOpenedEvent = "scene-opened";

    public const string SendMessagePath = "/chats/{chatName}/messages";

    public const string StreamMessagePath = "/chats/{chatName}/messages/stream";

    public const string ChatEventsPath = "/chats/{chatName}/events";

    public const string ChatTurnEvent = "chat-turn";

    public const string ChatDeltaEvent = "chat-delta";

    public const string BrainTopologyPath = "/brain/topology";
}
