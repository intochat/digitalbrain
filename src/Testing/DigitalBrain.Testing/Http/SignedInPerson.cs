namespace DigitalBrain.Testing;

public sealed record SignedInPerson(HttpClient Client, string Workspace, string Account) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
