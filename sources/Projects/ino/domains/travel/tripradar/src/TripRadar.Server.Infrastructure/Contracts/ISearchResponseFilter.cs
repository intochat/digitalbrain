using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Infrastructure.Contracts;

public interface ISearchResponseFilter<TResponse>
{
    TResponse Filter(TResponse response, IList<QueryColumn>? selectedColumns);
}
