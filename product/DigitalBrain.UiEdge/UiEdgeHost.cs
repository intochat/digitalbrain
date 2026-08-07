namespace DigitalBrain.UiEdge;

internal static class UiEdgeHost
{
    public static WebApplication MapUiEdgeHost(this WebApplication app)
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
