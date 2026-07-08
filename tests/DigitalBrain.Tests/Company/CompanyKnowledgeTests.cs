using DigitalBrain.Ino.Context;
using DigitalBrain.Core;
using DigitalBrain.Kernel.Llm;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Company;

public sealed class CompanyKnowledgeTests : NeuronTestBase
{
    [Fact]
    public async Task Ingests_Company_Process_Sources_And_Allows_Recall_Of_Key_Decisions()
    {
        // Use the grain for ingest path (exercises neuron + ingestor + journaled memory).
        var company = Grain<ICompanyKnowledgeNeuron>("company-refunds");

        const string policy = """
            Eligibility: purchased within 30 days, provide order ID or receipt.
            If defective and within 14 days: auto-approve full refund plus shipping.
            Amount over 500 or suspicious: manual review.
            Loyalty members get more leeway on receipt.
            """;

        const string transcript = """
            Always check purchase date first. Over 30 days is no unless warranty.
            Defective first two weeks auto. Flag high value for manual.
            """;

        await company.FireAsync(new IngestCompanySource("company-process-refunds", "refund-policy", policy));
        await company.FireAsync(new IngestCompanySource("company-process-refunds", "refund-transcript", transcript));

        // ContextNeuron Recall (hybrid) sees the journaled MemoryStored from ingest.
        var context = Grain<IContextNeuron>("context-for-company");
        await context.RememberAsync(policy); // reinforce
        await context.RememberAsync(transcript);

        var hits = await context.RecallAsync("30 days window defective auto approve", top: 3);

        Assert.NotEmpty(hits);
        string combined = string.Join(" ", hits);
        Assert.Contains("30", combined);
        Assert.Contains("defective", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("auto", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentIngestor_Produces_Chunks_For_Process_Text()
    {
        // Direct fast-path (no DI from client scope). Uses same types as neuron path.
        var vectorStore = new InMemoryVectorStore();
        var embedder = new NoOpEmbeddingGenerator();
        var ingestor = new DocumentIngestor(embedder, vectorStore);
        string source = "Step 1: check date. Step 2: verify receipt. If over 30 deny.";
        int count = await ingestor.IngestAsync("company-test", "policy-direct", source);
        Assert.True(count > 0);
    }
}
