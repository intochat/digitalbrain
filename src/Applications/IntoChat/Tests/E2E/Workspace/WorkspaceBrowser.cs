using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.Workspace;

/// <summary>Only project navigation and Flutter text-input readiness; no scenario orchestration.</summary>
internal static class WorkspaceBrowser
{
    public static async Task<string> CreateProjectAsync(IPage page, string title)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "New IntoChat project" }).ClickAsync();
        await EnterTextAsync(page.GetByRole(AriaRole.Textbox, new() { Name = "Project name (optional)" }), title);
        await page.GetByRole(AriaRole.Button, new() { Name = "Start project", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/projects/*/workspace*");
        var route = new Uri(page.Url);
        var segments = (route.Fragment.StartsWith("#/", StringComparison.Ordinal)
            ? route.Fragment[1..] : route.AbsolutePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(segments, "projects");
        Assert.True(index >= 0 && segments.Length > index + 1, "The UI did not navigate to its project workspace.");
        return Uri.UnescapeDataString(segments[index + 1]);
    }

    public static async Task EnterTextAsync(ILocator input, string text)
    {
        await input.ClickAsync();
        // Flutter attaches editing handlers in the semantics frame after focus.
        await input.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await input.FillAsync(text);
        await Assertions.Expect(input).ToHaveValueAsync(text);
    }
}
