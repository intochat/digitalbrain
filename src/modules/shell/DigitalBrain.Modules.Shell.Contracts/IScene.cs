using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[Alias("flutter.scene")]
[Description("Shell scene neuron")]
public partial interface IScene : INeuron;
