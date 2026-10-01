namespace IntoChat.Tests.E2E.LocalApps;

internal static class WorkspaceUploadFixture
{
    internal static async Task Upload(HttpClient http, string workspaceId, params IEnumerable<string> images)
    {
        foreach (var image in images)
        {
            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(image));
            using var response = await http.PostAsync($"/brains/{Uri.EscapeDataString(workspaceId)}/apps/files/upload?name={Uri.EscapeDataString(Path.GetFileName(image))}", content);
            response.EnsureSuccessStatusCode();
        }
    }
}
