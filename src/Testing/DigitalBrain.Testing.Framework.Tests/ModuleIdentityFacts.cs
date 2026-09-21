namespace DigitalBrain.Tests;

public sealed class ModuleIdentityFacts
{
    [Fact]
    public void ExcelAndFlutterUseDistinctSerializedSheetIdentities()
    {
        static string Identity(Type type) => type.GetCustomAttributes(typeof(Orleans.AliasAttribute), false)
            .Cast<Orleans.AliasAttribute>().Single().Alias;
        Assert.NotEqual(Identity(typeof(DigitalBrain.Excel.Spreadsheet.ISpreadsheet)),
            Identity(typeof(DigitalBrain.Flutter.Sheet.ISheet)));
    }
}
