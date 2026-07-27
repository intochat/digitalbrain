using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Ui;

[SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "Product Ui edge route and SSE names are the single public constant source for host, tests, and peers.")]
public static class UiEdgeContract
{
    public const string HealthPath = "/health";

    public const string HealthResponse = "healthy";

    public const string OpenScenePath = "/shells/{shellName}/scenes";

    public const string ShellEventsPath = "/shells/{shellName}/events";

    public const string ActivateControlPath = "/scenes/{sceneKey}/controls/{controlId}/activate";

    public const string AfterSequenceQuery = "afterSequence";

    public const string EventStreamContentType = "text/event-stream";

    public const string CacheControlNoCache = "no-cache";

    public const string SceneOpenedEvent = "scene-opened";

    public const string SendMessagePath = "/chats/{chatName}/messages";

    public const string ChatEventsPath = "/chats/{chatName}/events";

    public const string ChatTurnEvent = "chat-turn";
}
