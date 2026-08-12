namespace DigitalBrain.Abstractions;

// Interpreted kind tier: one grain type, N kind@instance keys.
// Address form: cell:{owner}/{kind}@{name}  (kind and name live in the grain key name part).
[ClientEntryPoint]
[Alias("db.cell")]
public partial interface ICell :
    INeuron,
    IHandle<CellApply>,
    IHandle<CellReset>
{
    const string GrainTypeName = "cell";
    const string DefaultInstanceName = "calculator@main";

    [Alias(nameof(Read))]
    Task<CellSnapshot> Read();
}
