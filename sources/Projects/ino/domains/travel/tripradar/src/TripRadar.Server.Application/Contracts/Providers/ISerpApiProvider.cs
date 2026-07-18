using System.Collections;

namespace TripRadar.Server.Application.Contracts.Providers;

public interface ISerpApiProvider
{
    Task<string?> FindAsync(Hashtable parameters, CancellationToken cancellationToken = default);
}
