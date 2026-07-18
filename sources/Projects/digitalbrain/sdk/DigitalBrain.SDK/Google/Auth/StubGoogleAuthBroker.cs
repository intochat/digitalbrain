using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.SDK.Google.Auth;

// In-domain stub used when DigitalBrain:Google:UseStubServices=true. Reports a
// stored token for every user EXCEPT those listed in DigitalBrain:Google:Stub:
// UsersWithoutTokens (comma-separated), enabling the "no token -> consent
// required" scenario without touching real Google APIs. AuthorizeAsync marks
// the user as authorized in memory so subsequent runs bypass consent.
public sealed class StubGoogleAuthBroker(IConfiguration configuration) : IGoogleAuthBroker
{
    static readonly char[] Separators = [',', ';'];
    private readonly HashSet<string> _authorizedUsers = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> HasStoredTokenAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        if (_authorizedUsers.Contains(userAccountId))
            return Task.FromResult(true);

        var withoutTokens = configuration["DigitalBrain:Google:Stub:UsersWithoutTokens"] ?? "";
        var excluded = withoutTokens
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(!excluded.Contains(userAccountId));
    }

    public Task AuthorizeAsync(
        string userAccountId, IReadOnlyCollection<string> scopes, CancellationToken ct)
    {
        _authorizedUsers.Add(userAccountId);
        return Task.CompletedTask;
    }
}
