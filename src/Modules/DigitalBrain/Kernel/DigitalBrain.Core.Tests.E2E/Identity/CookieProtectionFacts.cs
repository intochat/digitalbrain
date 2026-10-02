using System.Security.Claims;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using DigitalBrain.Contracts;
using DigitalBrain.Identity.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Core.Tests.E2E.Identity;

public sealed class CookieProtectionFacts(ReferenceBrainFixture host)
{
    [Fact(Timeout = 180_000)]
    public async Task BrowserLoginUsesTheCreatedProtectionContainerAndAnotherHostCanReadTheSession()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var options = new IdentityHostOptions();
        var storage = new BlobServiceClient(await brain.Application.GetConnectionStringAsync(DigitalBrainNames.GrainState, ct));
        var container = storage.GetBlobContainerClient(options.ProtectionContainerName);
        Assert.True((await container.ExistsAsync(ct)).Value);

        var principal = "protected-" + Guid.NewGuid().ToString("N");
        await using var browser = await brain.OpenBrowserAsync(ct);
        // Match the shell's browserKernelUri normalization so loopback aliases stay same-site.
        var runtime = new UriBuilder(brain.HttpClient.BaseAddress!) { Host = new Uri(browser.Page.Url).Host }.Uri.AbsoluteUri;
        var statuses = await browser.Page.EvaluateAsync<int[]>("""
            async ({ runtime, principal }) => {
                const post = path => fetch(new URL(path, runtime), {
                    method: 'POST', credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ principalId: principal, password: 'correct-password', displayName: 'Reader' })
                });
                const registered = await post('/identity/register');
                const logout = await post('/identity/logout');
                const login = await post('/identity/login');
                const session = await fetch(new URL('/identity/session', runtime), { credentials: 'include' });
                return [registered.status, logout.status, login.status, session.status];
            }
            """, new { runtime, principal }).WaitAsync(ct);
        Assert.Equal([200, 204, 200, 200], statuses);
        var cookie = Assert.Single(await browser.Page.Context.CookiesAsync([runtime]).WaitAsync(ct),
            cookie => cookie.Name == "digitalbrain.reference.session");
        Assert.True(cookie.HttpOnly);
        var keys = container.GetBlobClient("keys.xml");
        Assert.True((await keys.ExistsAsync(ct)).Value);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddDataProtection().SetApplicationName(options.ProtectionApplicationName).PersistKeysToAzureBlobStorage(keys);
        await using var provider = services.BuildServiceProvider();
        var authentication = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = authentication.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Value));
        Assert.NotNull(ticket);
        Assert.Equal(principal, ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }
}
