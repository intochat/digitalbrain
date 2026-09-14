using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Coding;

[assembly: NeuronJsonContext(typeof(CodingJson))]

namespace DigitalBrain.Coding;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OpenWorkspace))]
[JsonSerializable(typeof(ReloadWorkspace))]
[JsonSerializable(typeof(WorkspaceReceipt))]
[JsonSerializable(typeof(Accepted<WorkspaceReceipt>))]
[JsonSerializable(typeof(OpeningBody))]
[JsonSerializable(typeof(WorkspacePhase))]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(SymbolSearch))]
[JsonSerializable(typeof(SymbolHit))]
[JsonSerializable(typeof(SymbolSearchResult))]
[JsonSerializable(typeof(ReferenceSearch))]
[JsonSerializable(typeof(ReferenceHit))]
[JsonSerializable(typeof(ReferenceSearchResult))]
[JsonSerializable(typeof(DiagnosticsQuery))]
[JsonSerializable(typeof(DiagnosticHit))]
[JsonSerializable(typeof(DiagnosticsResult))]
[JsonSerializable(typeof(MapQuery))]
[JsonSerializable(typeof(ProjectNode))]
[JsonSerializable(typeof(ProjectEdge))]
[JsonSerializable(typeof(SolutionMap))]
public sealed partial class CodingJson : JsonSerializerContext;
