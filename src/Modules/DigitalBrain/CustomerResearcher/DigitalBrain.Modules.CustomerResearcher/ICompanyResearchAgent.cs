using DigitalBrain.Microsoft.Playwright;
using Microsoft.Extensions.AI;

namespace DigitalBrain.CustomerResearcher;

public interface ICompanyResearchAgent
{
    Task<ResearchResult> Research(string query, IPlaywright browser, CancellationToken ct);
    Task<ResearchResult> Research(string query, IPlaywright browser, Func<string, Task> progress, CancellationToken ct) => Research(query, browser, ct);
}
