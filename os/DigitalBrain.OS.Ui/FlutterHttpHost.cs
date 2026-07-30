namespace DigitalBrain.Flutter.Http;

internal static class FlutterHttpHost
{
    public static WebApplication MapFlutterHttpHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapUI();
        app.MapChat();
        app.MapAuthorizations();
        app.MapBehaviors();
        app.MapBehaviorEditorSurface();
        app.MapBrain();
        app.MapMcpOAuthCallback();
        return app;
    }
}
