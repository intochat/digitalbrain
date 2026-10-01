using System.Net.Http.Json;
using System.Security.Claims;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Sdk.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Tests.E2E.Identity;

public sealed class CookieProtectionFacts(ReferenceBrainFixture host)
{
    [Fact(Timeout = 180_000)]
    public async Task TheRuntimeCreatesItsProtectionContainerAndAnotherProcessCanReadTheSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var options = new IdentityHostOptions();
        var storage = new BlobServiceClient(await brain.Application.GetConnectionStringAsync(DigitalBrainNames.GrainState, ct));
        var container = storage.GetBlobContainerClient(options.ProtectionContainerName);
        Assert.True((await container.ExistsAsync(ct)).Value);

        var principal = "protected-" + Guid.NewGuid().ToString("N");
        using var client = new HttpClient { BaseAddress = brain.HttpClient.BaseAddress };
        using var registered = await client.PostAsJsonAsync("/identity/register", new { principalId = principal, password = "correct-password", displayName = "Reader" }, ct);
        registered.EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/identity/login", new { principalId = principal, password = "correct-password" }, ct);
        login.EnsureSuccessStatusCode();
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0].Split('=', 2)[1];
        var keys = container.GetBlobClient("keys.xml");
        Assert.True((await keys.ExistsAsync(ct)).Value);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName(options.ProtectionApplicationName).PersistKeysToAzureBlobStorage(keys);
        await using var provider = services.BuildServiceProvider();
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2");
        var ticket = new TicketDataFormat(protector).Unprotect(Uri.UnescapeDataString(cookie));
        Assert.NotNull(ticket);
        Assert.Equal(principal, ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }
}
