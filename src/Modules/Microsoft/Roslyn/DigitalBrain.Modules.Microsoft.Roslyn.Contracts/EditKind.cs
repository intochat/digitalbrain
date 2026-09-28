namespace DigitalBrain.Microsoft.Roslyn;

[GenerateSerializer]
[Alias("coding.edit-kind")]
public enum EditKind
{
    ReplaceMember = 0,
    InsertMember = 1,
    AddUsing = 2,
    ReplaceRange = 3,
    Rename = 4,
    ApplyCodeFix = 5,
}