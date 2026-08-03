using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.AccountEnrichment;

[Alias("account-enrichment")]
[Description("Sample account enrichment neuron")]
public partial interface IAccountEnrichment : INeuron;
