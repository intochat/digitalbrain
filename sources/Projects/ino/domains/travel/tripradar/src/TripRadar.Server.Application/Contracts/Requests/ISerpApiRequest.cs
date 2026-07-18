using System.Collections;

namespace TripRadar.Server.Application.Contracts.Requests;

public interface ISerpApiRequest
{
    Hashtable GetQueryParams();
}
