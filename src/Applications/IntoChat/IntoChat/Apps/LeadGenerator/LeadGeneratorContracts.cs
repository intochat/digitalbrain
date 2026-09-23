using DigitalBrain.Contracts;
using Orleans;
using Orleans.Concurrency;

namespace IntoChat.Apps;

// Company-level fields only: no contact names, emails, or LinkedIn-derived data. v1 sends no outreach.
[GenerateSerializer, Alias("intochat.leadgenerator-lead")]
public sealed record LeadRecord(
    [property: Id(0)] string Company,
    [property: Id(1)] string Address,
    [property: Id(2)] string Website,
    [property: Id(3)] string Phone,
    [property: Id(4)] string Category);

[GenerateSerializer, Alias("intochat.leadgenerator-sweep")]
public sealed record LeadSweepResult(
    [property: Id(0)] IReadOnlyList<LeadRecord> Leads,
    [property: Id(1)] string Query,
    [property: Id(2)] int MeteredRequests,
    [property: Id(3)] string WindowId);

[GenerateSerializer, Alias("intochat.leadgenerator-swept")]
public sealed record LeadGeneratorSwept(string WorkspaceId, string Query, int Leads) : Signal;

// The Leads live table in the app's own data, keyed by the workspace scope id.
[Alias("intochat.leadgenerator-leads"), Orleans.Metadata.DefaultGrainType("intochat.leadgenerator-leads")]
public interface ILeadGeneratorLeads : INeuron
{
    Task<LeadSweepResult> Sweep(string query);

    [ReadOnly] Task<LeadSweepResult> Read();
}
