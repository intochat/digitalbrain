using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.CSharpExpert;

[Alias("csharp-expert.profile-grain")]
public interface ICodingProfile : INeuron
{
    [ReadOnly]
    Task<CodingProfile> Read();

    Task Write(CodingProfile profile);
}
