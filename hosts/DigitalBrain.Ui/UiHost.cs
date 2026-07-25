namespace DigitalBrain.Ui;

internal static class UiHost
{
    public static WebApplication MapUiHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(UiEdgeContract.HealthPath, static () => Results.Ok(UiEdgeContract.HealthResponse));
        app.MapUi();
        return app;
    }
}
