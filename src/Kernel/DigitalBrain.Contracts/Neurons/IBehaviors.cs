using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.behaviors")]
public partial interface IBehaviors :
    INeuron,
    IHandle<AdmitBehavior>,
    IHandle<RemoveBehavior>,
    IHandle<ReadBehaviors>,
    IHandle<ReportBehaviorStatus>;
