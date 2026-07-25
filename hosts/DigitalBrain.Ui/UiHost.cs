namespace DigitalBrain.Ui;

internal static class UiHost
{
    public static WebApplication MapUiHost(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", static () => Results.Ok("healthy"));
        app.MapUi();
        return app;
    }
}
