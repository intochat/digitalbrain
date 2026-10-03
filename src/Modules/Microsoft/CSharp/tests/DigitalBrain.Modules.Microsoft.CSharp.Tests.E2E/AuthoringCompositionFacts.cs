using System.Net;
using DigitalBrain.Microsoft.CSharp;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.E2E;

public sealed class AuthoringCompositionFacts
{
    [Fact]
    public async Task CSharpConsoleIsAvailableOnlyWhenTheHostComposesAuthoring()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var on = await E2ETest.Create().WithModule<CSharpAuthoringModule>().StartAsync(ct);
        using var available = await on.HttpClient.GetAsync($"/brains/{on.WorkspaceId}/csharp/", ct);
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        await using var off = await E2ETest.Create().WithModule<DigitalBrain.Time.TimeModule>().StartAsync(ct);
        using var unavailable = await off.HttpClient.GetAsync($"/brains/{off.WorkspaceId}/csharp/?developerMode=true", ct);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }
}
