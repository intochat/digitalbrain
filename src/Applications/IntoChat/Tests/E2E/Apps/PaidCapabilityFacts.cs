using System.Net.Http.Json;
using DigitalBrain.Compute;
using DigitalBrain.Flutter.Collection;
using IntoChat.Apps;
using IntoChat.Workspace;

namespace IntoChat.Tests.E2E.Apps;

// J4: the first paid capability. The Image Editor's BackgroundRemover sees only the selected
// images, shows a plan with an estimate and a maximum, runs under an "Allow once" allowance, and
// the receipt charges only the images that succeeded. Every original is kept and a new version
// appears for each success.
public sealed class PaidCapabilityFacts
{
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

    [Fact(Timeout = 300_000)]
    public async Task ThreePhotosPlanAllowOnceChargeNineAndKeepOriginals()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-paid-").FullName;
        try
        {
            foreach (var name in new[] { "photo-1.png", "photo-2.png", "photo-3.png" })
            {
                await File.WriteAllBytesAsync(Path.Combine(root, name), Convert.FromBase64String(OnePixelPng), ct);
            }

            await using var brain = await IntoChatE2ETest.Create(privateConfiguration: new()
            {
                ["IntoChat:LocalFiles:Roots:downloads"] = root,
                ["IntoChat:LocalFiles:AssetDirectory"] = Path.Combine(root, "assets"),
                // The third product photo fails as a third-party fault and must not be charged.
                ["IntoChat:BackgroundRemoval:FailingImages:0"] = "photo-3.png",
            }).StartAsync(ct);

            var scope = WorkspaceScope.Create("owner", "paid-capability").Id;
            var files = brain.Get<IFileExplorer>(scope);
            await files.Navigate(null);
            var entries = (await brain.Get<ICollectionView>(scope + "/apps/files/items").Read()).Definition.Items;
            Assert.Equal(3, entries.Count);

            var documentIds = new List<string>();
            var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var opened = await files.OpenImage(entry.Id);
                documentIds.Add(opened.Id);
                nameById[opened.Id] = opened.Asset!.Name;
            }

            // The plan card is the product entry point: the app sees only the selected images.
            var plan = await PostAsync<BackgroundRemovalPlan>(brain.HttpClient, "plan", new { imageIds = documentIds, intentId = "intent-paid-1" });

            Assert.Equal(documentIds.Count, plan.Images.Count);
            Assert.Equal([.. documentIds], plan.Images.Select(image => image.ImageId));
            Assert.Equal(12m, plan.EstimatedCompute);
            Assert.Equal(20m, plan.MaximumCompute);
            Assert.NotNull(plan.ApprovalId);
            Assert.Contains(plan.Options, option => option.Scope == AllowanceScope.Once && option.LimitCompute == 20m);
            Assert.Contains(plan.Options, option => option.Scope == AllowanceScope.Always && option.LimitCompute == 100m);

            // No allowance yet: a direct run must ask for approval and charge nothing.
            var blocked = await PostAsync<BackgroundRemovalReceipt>(brain.HttpClient, "run", new { planId = plan.PlanId, intentId = "intent-paid-1" });
            Assert.True(blocked.RequiresApproval);
            Assert.Equal(0m, blocked.ChargedCompute);

            // "Allow once" is the P3.1 allowance path.
            await PostAsync<BackgroundRemovalPlan>(brain.HttpClient, "approve", new { planId = plan.PlanId, scope = (int)AllowanceScope.Once });

            var receipt = await PostAsync<BackgroundRemovalReceipt>(brain.HttpClient, "run", new { planId = plan.PlanId, intentId = "intent-paid-1" });

            Assert.False(receipt.RequiresApproval);
            Assert.Equal(9m, receipt.ChargedCompute);
            Assert.Equal(2, receipt.Images.Count(image => image.Succeeded));
            var failed = Assert.Single(receipt.Images, image => !image.Succeeded);
            Assert.Equal("photo-3.png", nameById[failed.ImageId]);
            Assert.Equal(0m, failed.ChargedCompute);
            Assert.NotNull(failed.FailureReason);

            // Originals are kept; a new version appears only for each success.
            foreach (var id in documentIds)
            {
                var document = await brain.Get<IImageDocument>(scope + "/images/" + id).Read();
                Assert.Contains(document.Versions, version => version.Kind == "original");
            }
            foreach (var succeeded in receipt.Images.Where(image => image.Succeeded))
            {
                var document = await brain.Get<IImageDocument>(scope + "/images/" + succeeded.ImageId).Read();
                Assert.Equal(2, document.Versions.Count);
                Assert.Contains(document.Versions, version => version.Kind == "background-removed");
            }
            var failedDocument = await brain.Get<IImageDocument>(scope + "/images/" + failed.ImageId).Read();
            Assert.Single(failedDocument.Versions);

            var report = await brain.Get<IAllowanceLedger>(plan.AccountId).ReadLimitsAsync(ct);
            Assert.Equal(9m, report.SpentCompute);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<T> PostAsync<T>(HttpClient http, string action, object payload)
    {
        using var response = await http.PostAsJsonAsync($"/workspaces/paid-capability/apps/background-removal/{action}", payload, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }
}
