using System.Collections;

namespace TripRadar.Server.Application.DTO
{
    public class SerpApiBaseRequest
    {
        protected static void AddIfNotNull(string key, object? value, Hashtable hashtable)
        {
            if (value != null && !string.IsNullOrWhiteSpace(value.ToString())) 
                hashtable[key] = value;
        }
    }
}