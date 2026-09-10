namespace DigitalBrain.Excel;

public sealed class ExcelModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // NativeTools contributor lands after the AI module merge
    }
}
