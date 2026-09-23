using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Receipts;
using DigitalBrain.Receipts.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ReceiptFacts
{
    [Fact]
    public async Task WriteStoresOneDurableRowPerIntentPublishesAContentFreeEventAndKeeps()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<ReceiptsModule>().StartAsync(ct);
        var receipt = brain.Get<IReceipt>("intent-1");
        await using var events = await brain.Observe<IntentRecorded>(receipt, ct);

        await receipt.Write(Draft(ReceiptOutcome.Succeeded, modelCalls: 2, compute: 4.5m));
        var stored = await receipt.Read();
        Assert.NotNull(stored);
        Assert.Equal("intent-1", stored!.IntentId);
        Assert.True(stored.Content.ShadowPriced);
        Assert.Equal(ReceiptOutcome.Succeeded, stored.Content.Outcome);
        Assert.False(stored.Kept);

        // One durable row per intent: a second write for the same intent is ignored.
        await receipt.Write(Draft(ReceiptOutcome.Failed, modelCalls: 9, compute: 99m));
        Assert.Equal(ReceiptOutcome.Succeeded, (await receipt.Read())!.Content.Outcome);

        var recorded = await events.NextAsync(ct: ct);
        Assert.Equal("intent-1", recorded.IntentId);
        Assert.Equal(ReceiptOutcome.Succeeded, recorded.Outcome);
        Assert.Equal(2, recorded.ModelCalls);
        Assert.Equal(4.5m, recorded.Compute);

        await receipt.MarkKept();
        Assert.True((await receipt.Read())!.Kept);
        await brain.DeactivateAsync(receipt, ct);
        Assert.True((await receipt.Read())!.Kept);
    }

    [Fact]
    public void PriceBookPricesConcreteModelsPerMillionAndLeavesLocalAndUnknownAtZero()
    {
        var book = new PriceBook();
        Assert.Equal("v0", book.Version);
        Assert.Equal(250m, book.PriceInCompute(PriceBook.ChatMeterId("gpt-5.6-luna", "input"), 1_000_000m));
        Assert.Equal(125m, book.PriceInCompute(PriceBook.ChatMeterId("gpt-5.6-luna", "cached"), 1_000_000m));
        Assert.Equal(0m, book.PriceInCompute(PriceBook.ChatMeterId("gemma4:e2b", "input"), 1_000_000m));
        Assert.True(book.IsLocal("gemma4:e2b"));
        Assert.False(book.IsLocal("gpt-5.6-luna"));
        Assert.Equal(0m, book.PriceInCompute("chat:unknown-model:input", 1_000_000m));
        Assert.Equal(0m, book.PriceInCompute(PriceBook.EmbeddingMeterId("embeddinggemma"), 1_000_000m));
    }

    [Fact]
    public void ExactlyOnePriceBookFileExistsAndItLivesUnderTheComputeModule()
    {
        var root = FindRepositoryRoot();
        var priceBooks = Directory.GetFiles(Path.Combine(root, "src"), "pricebook*.json", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToArray();
        var priceBook = Assert.Single(priceBooks);
        Assert.Contains(Path.Combine("src", "Modules", "Compute"), priceBook, StringComparison.Ordinal);
    }

    private static ReceiptDraft Draft(ReceiptOutcome outcome, int modelCalls, decimal compute) => new()
    {
        WorkspaceId = "workspace",
        ConversationId = "thread",
        Outcome = outcome,
        Summary = "Workspace intent",
        ModelCalls = modelCalls,
        EstimatedCompute = compute,
        ActualCompute = compute,
        ShadowPriced = true,
        FirstTry = outcome == ReceiptOutcome.Succeeded,
    };

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        { directory = directory.Parent!; }
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
