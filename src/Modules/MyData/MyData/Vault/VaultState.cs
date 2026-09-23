using DigitalBrain.Contracts.Types;

namespace DigitalBrain.MyData;

[GenerateSerializer, Alias("mydata.state")]
internal sealed class VaultState
{
    [Id(0)] public string Owner { get; set; } = "";
    [Id(1)] public string WrappedOwnerKey { get; set; } = "";
    [Id(2)] public Dictionary<string, VaultFieldRecord> Fields { get; set; } = [];
    [Id(3)] public List<VaultAuditRecord> Audit { get; set; } = [];
}

[GenerateSerializer, Alias("mydata.field-record")]
internal sealed class VaultFieldRecord
{
    [Id(0)] public string FieldPath { get; set; } = "";
    [Id(1)] public FieldKind Kind { get; set; }
    [Id(2)] public bool IsSecret { get; set; }
    [Id(3)] public string Label { get; set; } = "";
    [Id(4)] public bool IsSet { get; set; }
    [Id(5)] public string SealedValue { get; set; } = "";
    [Id(6)] public string SealedCredentialKey { get; set; } = "";
    [Id(7)] public string SealedSecret { get; set; } = "";
}

[GenerateSerializer, Alias("mydata.audit-record")]
internal sealed class VaultAuditRecord
{
    [Id(0)] public string Action { get; set; } = "";
    [Id(1)] public string FieldPath { get; set; } = "";
    [Id(2)] public string PrincipalId { get; set; } = "";
    [Id(3)] public string? AppId { get; set; }
    [Id(4)] public DateTimeOffset At { get; set; }
}
