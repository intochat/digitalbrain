using DigitalBrain.Access;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Hosting;

[NeuronAlias("programs"), DefaultNeuronType("digitalbrain.programs")]
[Internal]
public interface IPrograms
{
    [Interleaves] Task<ProgramView> Assign(string owner, Dictionary<string, string> sources, Dictionary<string, string> settings);
    [Interleaves] Task<ProgramView> AssignPrebuilt(string owner, Dictionary<string, PrebuiltBehavior> behaviors, Dictionary<string, string> settings);
    [Interleaves] Task Stage(string hash, int offset, byte[] chunk, bool last);
    Task Retire(string owner);
    [Interleaves] Task<ProgramView?> Settled(string owner, TimeSpan timeout);
    [Interleaves] Task<ProgramView?> Describe(string owner);
}
