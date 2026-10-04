using System.Net;
using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests;

public sealed class AuthoringCompositionFacts
{
    [Fact(Timeout = 120_000)]
    public async Task CSharpConsoleIsAvailableOnlyWhenTheHostComposesAuthoring()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var on = await ModuleTest.Create().WithModule<CSharpAuthoringModule>().WithHttpEdge().StartAsync(ct);
        using var available = await on.HttpClient.GetAsync("/brains/authoring-on/csharp/", ct);
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        await using var off = await ModuleTest.Create().WithModule<DigitalBrain.Time.TimeModule>().WithReminders().WithHttpEdge().StartAsync(ct);
        using var unavailable = await off.HttpClient.GetAsync("/brains/authoring-off/csharp/?developerMode=true", ct);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }
}
