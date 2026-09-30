using System.Security.Cryptography;
using Orleans;

namespace DigitalBrain.Flutter.Workspace;

internal interface IShellPart : IGrainWithStringKey
{
    Task Put(string json);
    Task<string> Read();
}
