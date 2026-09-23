using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

[GenerateSerializer, Alias("mydata.field-view")]
public sealed record VaultFieldView
{
    [Id(0)] public string FieldPath { get; init; } = "";
    [Id(1)] public FieldKind Kind { get; init; }
    [Id(2)] public string Sensitivity { get; init; } = "";
    [Id(3)] public bool IsSet { get; init; }
    [Id(4)] public string Display { get; init; } = "";
    [Id(5)] public bool IsSecret { get; init; }
}

[GenerateSerializer, Alias("mydata.view")]
public sealed record VaultView
{
    [Id(0)] public string Owner { get; init; } = "";
    [Id(1)] public VaultFieldView[] Fields { get; init; } = [];
    [Id(2)] public bool Erased { get; init; }
}

[GenerateSerializer, Alias("mydata.export-field")]
public sealed record VaultExportField
{
    [Id(0)] public string FieldPath { get; init; } = "";
    [Id(1)] public FieldKind Kind { get; init; }
    [Id(2)] public bool IsSecret { get; init; }
    [Id(3)] public string? Value { get; init; }
}

[GenerateSerializer, Alias("mydata.export")]
public sealed record VaultExport
{
    [Id(0)] public string Owner { get; init; } = "";
    [Id(1)] public VaultExportField[] Fields { get; init; } = [];
}

[GenerateSerializer, Alias("mydata.audit-entry")]
public sealed record VaultAuditEntry
{
    [Id(0)] public string Action { get; init; } = "";
    [Id(1)] public string FieldPath { get; init; } = "";
    [Id(2)] public string PrincipalId { get; init; } = "";
    [Id(3)] public string? AppId { get; init; }
    [Id(4)] public DateTimeOffset At { get; init; }
}
