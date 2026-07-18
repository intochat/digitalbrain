using Ino.Core;

namespace Ino.Core.Hosting;

public interface IFirePort
{
    Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse;
    Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse;
}
