namespace DigitalBrain.Contracts;

/// <summary>
/// The minimal Phase-0 sensitivity tag carried with a request. Unknown is the default and never
/// captures; the semantic type catalog (P1.1) replaces this tag with typed values.
/// </summary>
public enum ContentClass
{
    Unknown = 0,
    Ordinary = 1,
    Personal = 2,
    Credential = 3,
}

/// <summary>
/// The ambient capture decision for the user request being served. Stamped only at a trusted edge
/// (the endpoint that starts an intent) and read by the AI pipeline when it builds a turn's model
/// client, so content capture is scoped to that request rather than to the whole deployment.
/// Capture is allowed only for the local owner's ordinary dev runs: hosted runs, other principals
/// and Personal/Credential values all stay off, and an unknown identity or class defaults off.
/// </summary>
public sealed record ContentCaptureScope(bool LocalOwner, ContentClass Class) : IDisposable
{
    private static readonly AsyncLocal<ContentCaptureScope?> Ambient = new();
    private ContentCaptureScope? _previous;
    private bool _disposed;

    public static ContentCaptureScope? Current => Ambient.Value;

    public bool AllowsCapture => LocalOwner && Class == ContentClass.Ordinary;

    public static ContentCaptureScope Begin(bool localOwner, ContentClass @class)
    {
        var scope = new ContentCaptureScope(localOwner, @class) { _previous = Ambient.Value };
        Ambient.Value = scope;
        return scope;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        Ambient.Value = _previous;
    }
}