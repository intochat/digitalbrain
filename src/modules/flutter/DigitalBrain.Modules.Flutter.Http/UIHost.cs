namespace DigitalBrain.UI;

internal static class UIHost
{
    public static WebApplication MapUIHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapUI();
        app.MapChat();
        return app;
    }
}
