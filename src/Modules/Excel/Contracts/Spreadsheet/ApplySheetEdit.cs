namespace DigitalBrain.Excel.Spreadsheet;

/// <summary>A replacement grid or cell edit to apply, with exactly one set.</summary>
[GenerateSerializer]
[Alias("excel.apply-sheet-edit")]
public sealed record ApplySheetEdit(
    [property: Id(0)] ExcelState? Replace,
    [property: Id(1)] CellEdit? Cell);