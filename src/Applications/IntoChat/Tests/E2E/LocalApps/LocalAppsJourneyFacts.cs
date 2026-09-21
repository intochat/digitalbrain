using System.Buffers.Binary;
using System.Security.Cryptography;
using DigitalBrain.Flutter;
using IntoChat.Tests.E2E.Workspace;
using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.LocalApps;

public sealed class LocalAppsJourneyFacts
{
    [Fact(Timeout = 300_000)]
    public async Task DownloadsImageCanBeDrawnCroppedAndSavedWithoutChangingOriginal()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("intochat-local-journey-").FullName;
        try
        {
            var downloads = Directory.CreateDirectory(Path.Combine(root, "Downloads")).FullName;
            var original = Path.Combine(downloads, "Untitled.png");
            var source = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAHgAAABQCAIAAABd+SbeAAAAxUlEQVR4nO3QQREAMAjAMPybHi6yB42CXueFmN8BVzQaaTTSaKTRSKORRiONRhqNNBppNNJopNFIo5FGI41GGo00Gmk00mik0UijkUYjjUYajTQaaTTSaKTRSKORRiONRhqNNBppNNJopNFIo5FGI41GGo00Gmk00mik0UijkUYjjUYajTQaaTTSaKTRSKORRiONRhqNNBppNNJopNFIo5FGI41GGo00Gmk00mik0UijkUYjjUYajTQaaTTSaKTRSKORRiMLMEMWEfUbSmoAAAAASUVORK5CYII=");
            var acceptanceImage = Environment.GetEnvironmentVariable("INTOCHAT_LOCAL_ACCEPTANCE_IMAGE");
            if (!string.IsNullOrWhiteSpace(acceptanceImage))
            {
                original = Path.GetFullPath(acceptanceImage);
                downloads = Path.GetDirectoryName(original)!;
                source = await File.ReadAllBytesAsync(original, ct);
            }
            else
            {
                await File.WriteAllBytesAsync(original, source, ct);
                await File.WriteAllTextAsync(Path.Combine(downloads, "readme.txt"), "Local file fixture", ct);
            }
            var outputName = Path.GetFileNameWithoutExtension(original) + "-edited.png";
            for (var suffix = 1; File.Exists(Path.Combine(downloads, outputName)); suffix++) { outputName = Path.GetFileNameWithoutExtension(original) + "-edited (" + suffix + ").png"; }
            var settings = new Dictionary<string, string?> { ["IntoChat:LocalFiles:Roots:downloads"] = downloads, ["IntoChat:LocalFiles:AssetDirectory"] = Path.Combine(root, "assets") };
            await using var brain = await IntoChatE2ETest.Create(privateConfiguration: settings)
                .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
            var page = brain.Page;
            page.SetDefaultTimeout(15000);
            await page.SetViewportSizeAsync(1600, 1000);
            await page.GetByRole(AriaRole.Button, new() { Name = "Applications", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Menuitem, new() { Name = "Files On this computer", Exact = true }).ClickAsync();
            if (!string.IsNullOrWhiteSpace(acceptanceImage))
            {
                var filter = page.GetByRole(AriaRole.Textbox, new() { Name = "Filter files", Exact = true });
                await WorkspaceBrowser.EnterTextAsync(filter, Path.GetFileNameWithoutExtension(original));
                await filter.PressAsync("Enter");
            }
            await page.GetByRole(AriaRole.Button, new() { Name = "Open " + Path.GetFileName(original), Exact = true }).ClickAsync();
            var editor = page.GetByRole(AriaRole.Region, new() { Name = "Image Editor", Exact = true });
            await Assertions.Expect(editor).ToBeVisibleAsync();
            await editor.GetByRole(AriaRole.Button, new() { Name = "100%", Exact = true }).ClickAsync();
            await editor.GetByRole(AriaRole.Button, new() { Name = "Draw", Exact = true }).ClickAsync();
            var canvas = editor.GetByRole(AriaRole.Region, new() { Name = "Image canvas", Exact = true });
            var box = await canvas.BoundingBoxAsync() ?? throw new InvalidOperationException("Image canvas has no bounds.");
            await page.Mouse.MoveAsync(box.X + 20, box.Y + 20);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(box.X + 40, box.Y + 20, new() { Steps = 5 });
            await page.Mouse.UpAsync();
            await Assertions.Expect(editor.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true })).ToBeEnabledAsync();
            await editor.GetByRole(AriaRole.Button, new() { Name = "Crop", Exact = true }).ClickAsync();
            foreach (var (label, value) in new[] { ("X", "10"), ("Y", "10"), ("Width", "60"), ("Height", "40") })
            { await WorkspaceBrowser.EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = label, Exact = true }), value); }
            await page.GetByRole(AriaRole.Button, new() { Name = "Apply crop", Exact = true }).ClickAsync();
            await editor.GetByRole(AriaRole.Button, new() { Name = "Save copy", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("Saved " + outputName, new() { Exact = true })).ToBeVisibleAsync();
            var output = await File.ReadAllBytesAsync(Path.Combine(downloads, outputName), ct);
            Assert.Equal(60, BinaryPrimitives.ReadInt32BigEndian(output.AsSpan(16, 4)));
            Assert.Equal(40, BinaryPrimitives.ReadInt32BigEndian(output.AsSpan(20, 4)));
            Assert.Equal(SHA256.HashData(source), SHA256.HashData(await File.ReadAllBytesAsync(original, ct)));
            var pixel = await page.EvaluateAsync<int[]>("""
                async base64 => {
                  const image = new Image(); image.src = 'data:image/png;base64,' + base64; await image.decode();
                  const canvas = document.createElement('canvas'); canvas.width=image.width;canvas.height=image.height;
                  const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);
                  return [...ctx.getImageData(15,10,1,1).data];
                }
                """, Convert.ToBase64String(output));
            Assert.Equal(255, pixel[0]); Assert.InRange(pixel[1], 90, 120);
            var screenshot = Environment.GetEnvironmentVariable("INTOCHAT_LOCAL_ACCEPTANCE_SCREENSHOT");
            if (!string.IsNullOrWhiteSpace(screenshot)) { await page.ScreenshotAsync(new() { Path = screenshot }); }
            await page.GetByRole(AriaRole.Button, new() { Name = "Files", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Open " + outputName, Exact = true })).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(root, "local-apps.png") });
        }
        finally { Directory.Delete(root, true); }
    }
}
