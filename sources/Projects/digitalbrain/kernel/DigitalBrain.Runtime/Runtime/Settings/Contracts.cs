using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Runtime.Runtime.Settings
{
    [GenerateSerializer]
    public sealed record RequestSetting([property: Id(1)] string Scope,
    [property: Id(2)] string Key
) : Synapse;

    [GenerateSerializer]
    public sealed record UpdateSetting([property: Id(1)] string Scope,
    [property: Id(2)] string Key,
    [property: Id(3)] string Value
) : Synapse;

    [GenerateSerializer]
    public sealed record RequestSettingsCard : Synapse;

    [Signal(Fqn)]
    [GenerateSerializer]
    public sealed record SettingResult(
        [property: Id(0)] string Scope,
        [property: Id(1)] string Key,
        [property: Id(2)] string Value) : Synapse
    {
        public const string Fqn = "DigitalBrain.Settings.SettingResult";
    }

    [Signal(Fqn)]
    [GenerateSerializer]
    public sealed record SettingChanged(
        [property: Id(0)] string Scope,
        [property: Id(1)] string Key,
        [property: Id(2)] string Value) : Synapse
    {
        public const string Fqn = "DigitalBrain.Settings.SettingChanged";
    }
}

namespace DigitalBrain.Kernel.Settings
{
    [GenerateSerializer]
    public sealed record RequestPrivateSetting([property: Id(1)] string Token,
    [property: Id(2)] string Scope,
    [property: Id(3)] string Key
) : Synapse;

    [GenerateSerializer]
    public sealed record UpdatePrivateSetting([property: Id(1)] string Token,
    [property: Id(2)] string Scope,
    [property: Id(3)] string Key,
    [property: Id(4)] string Value
) : Synapse;

    [Signal(Fqn)]
    [GenerateSerializer]
    public sealed record PrivateSettingResult(
        [property: Id(0)] string Scope,
        [property: Id(1)] string Key,
        [property: Id(2)] string Value) : Synapse
    {
        public const string Fqn = "DigitalBrain.Kernel.Settings.PrivateSettingResult";
    }
}
