namespace IntoChat.Tests.E2E.Packages;

internal sealed record SignedInPerson(HttpClient Client, string Workspace) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
