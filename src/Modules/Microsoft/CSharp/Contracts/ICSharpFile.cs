using DigitalBrain.Contracts;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.CSharp;

[Alias("microsoft.csharp.file"), DefaultGrainType("microsoft.csharp.file")]
public interface ICSharpFile : INeuron
{
    Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default);
    Task Write(string source, CancellationToken cancellationToken = default);
    Task Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
    // The first start builds the sandbox image; Start keeps the file running until Stop, a clean exit or repeated crashes.
    [ResponseTimeout("00:05:00")] Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default);
    [ResponseTimeout("00:05:00")] Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default);
    Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default);
    [ResponseTimeout("00:05:00")] Task Delete(CancellationToken cancellationToken = default);
}
