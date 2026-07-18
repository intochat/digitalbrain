namespace DigitalBrain.Runtime;

// Kernel-level synapse type name constants.
// Dynamic-domain types (CreateNeuronRequest, NeuronCreated, Plan*) live in
// DigitalBrain.Runtime.Dynamic.DynamicSynapseTypes.
public static class SynapseTypeNames
{
    public const string RequestSetting = "DigitalBrain.Settings.RequestSetting";
    public const string UpdateSetting = "DigitalBrain.Settings.UpdateSetting";
    public const string SettingResult = "DigitalBrain.Settings.SettingResult";
    public const string SettingChanged = "DigitalBrain.Settings.SettingChanged";

    public const string RequestPrivateSetting = "DigitalBrain.Kernel.Settings.RequestPrivateSetting";
    public const string UpdatePrivateSetting = "DigitalBrain.Kernel.Settings.UpdatePrivateSetting";
    public const string PrivateSettingResult = "DigitalBrain.Kernel.Settings.PrivateSettingResult";
}
