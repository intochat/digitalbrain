using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Excel;

[assembly: NeuronJsonContext(typeof(ExcelJson))]

namespace DigitalBrain.Excel;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ExcelState))]
[JsonSerializable(typeof(ExcelRow))]
[JsonSerializable(typeof(ApplySheetEdit))]
[JsonSerializable(typeof(CellEdit))]
[JsonSerializable(typeof(SheetVersion))]
[JsonSerializable(typeof(ReadRange))]
[JsonSerializable(typeof(SheetRange))]
[JsonSerializable(typeof(ApplyingBody))]
[JsonSerializable(typeof(SheetChangedBody))]
[JsonSerializable(typeof(Accepted<SheetVersion>))]
public sealed partial class ExcelJson : JsonSerializerContext;
