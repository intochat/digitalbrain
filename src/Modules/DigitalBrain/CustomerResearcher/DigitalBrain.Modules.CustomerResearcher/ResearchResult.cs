using Microsoft.Extensions.AI;

namespace DigitalBrain.CustomerResearcher;

public sealed record ResearchResult(CompanyResearch? Company, string Status);
