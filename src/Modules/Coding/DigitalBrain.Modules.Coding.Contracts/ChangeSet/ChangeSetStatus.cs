namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.change-set-status")]
public enum ChangeSetStatus
{
    Draft = 0,
    Checked = 1,
    Committed = 2,
    Discarded = 3,
}
