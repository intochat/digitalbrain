using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Apps;

// An app someone is creating by describing it. The Author agent writes its spec in plain language;
// the person reads and edits it; the Builder agent then writes the tests and the implementation, and
// the app is published only once its tests run green. Keyed "{owner}/drafts/{id}".
[Alias("intochat.app-draft"), Orleans.Metadata.DefaultGrainType("intochat.app-draft")]
public interface IAppDraft : INeuron
{
    [ResponseTimeout("00:10:00")] Task<AppDraftView> Draft(string request);
    [ResponseTimeout("00:10:00")] Task<AppDraftView> Revise(string instruction);
    Task<AppDraftView> EditSpec(string spec);
    Task<AppDraftView> SaveDocument(SaveAppDocument request);
    Task<AppDraftView> ImportRevision(PackageRevisionRef revision, long expectedRevision);
    Task<AppAuthoringDocument> ProposeConversion(long expectedRevision);
    [ResponseTimeout("02:00:00")] Task<AppDraftView> Build();
    [ReadOnly, AlwaysInterleave] Task<AppDraftView> Read();
}

