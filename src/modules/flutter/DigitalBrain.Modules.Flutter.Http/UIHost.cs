namespace DigitalBrain.UI;

internal static class UIHost
{
    public static WebApplication MapUIHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(UIEdgeContract.HealthPath, static () => Results.Ok(UIEdgeContract.HealthResponse));
        app.MapUI();
        app.MapChat();
        return app;
    }
}
