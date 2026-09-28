using DigitalBrain.Core;

namespace DigitalBrain.Apps;

public static class ApplicationComposition
{
    public static BrainCompositionBuilder WithApp(this BrainCompositionBuilder composition, AppDefinition app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return composition.RequireModules(app.RequiredModules);
    }
}
