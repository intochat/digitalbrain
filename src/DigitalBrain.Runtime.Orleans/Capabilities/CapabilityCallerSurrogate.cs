using DigitalBrain.Capabilities;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>Orleans wire form of <see cref="CapabilityCaller"/>. The core record stays free of Orleans types.</summary>
[GenerateSerializer]
internal struct CapabilityCallerSurrogate
{
    [Id(0)] public string PrincipalId;
    [Id(1)] public string AccountId;
    [Id(2)] public string BrainId;
    [Id(3)] public string? AppId;
    [Id(4)] public string? ConversationId;
}

[RegisterConverter]
internal sealed class CapabilityCallerSurrogateConverter : IConverter<CapabilityCaller, CapabilityCallerSurrogate>
{
    public CapabilityCaller ConvertFromSurrogate(in CapabilityCallerSurrogate surrogate)
        => new(surrogate.PrincipalId, surrogate.AccountId, surrogate.BrainId, surrogate.AppId, surrogate.ConversationId);

    public CapabilityCallerSurrogate ConvertToSurrogate(in CapabilityCaller value) => new()
    {
        PrincipalId = value.PrincipalId,
        AccountId = value.AccountId,
        BrainId = value.BrainId,
        AppId = value.AppId,
        ConversationId = value.ConversationId,
    };
}
