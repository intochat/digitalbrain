using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

// How an app that is configuration on top of neurons answers an invocation, for example a group chat
// app asking IGroupChat. The host registers one per runtime name; "csharp" apps answer from their script instead.
public interface IAppRuntime
{
    string Name { get; }
    string AuthoringDescription { get; }
    Task<string> Answer(AppRuntimeRequest request, CancellationToken cancellationToken);
}
