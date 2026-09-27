using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.CSharp;

[Alias("microsoft.csharp.file"), DefaultGrainType("microsoft.csharp.file")]
public interface ICSharpFile : INeuron
{
    Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Write(string source, CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default);
    Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default);
    Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default);
    Task Delete(CancellationToken cancellationToken = default);
}
