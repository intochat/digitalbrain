using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[Description("Owner-scoped Salesforce neuron identified by module-owned connection name")]
[Alias("DigitalBrain.Salesforce.ISalesforce")]
public interface ISalesforce : INeuron;
