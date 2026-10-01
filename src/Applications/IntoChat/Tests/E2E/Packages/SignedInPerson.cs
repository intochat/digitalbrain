namespace IntoChat.Tests.E2E.Packages;

internal sealed record SignedInPerson(HttpClient Client, string Workspace, string Account) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
