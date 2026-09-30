using System.Security.Cryptography;
using Orleans;

namespace DigitalBrain.Flutter.Workspace;

internal interface IShellState : IGrainWithStringKey
{
    Task<ShellRead> Read();
    Task<ShellRead> Save(long expectedRevision, string operationId, string json);
}
