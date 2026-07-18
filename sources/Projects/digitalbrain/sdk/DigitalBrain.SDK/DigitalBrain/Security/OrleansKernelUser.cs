namespace DigitalBrain.SDK.DigitalBrain.Security;

public sealed class OrleansKernelUser : IKernelUser
{
    public string UserId => GetCurrentUsername();
    public string Username => GetCurrentUsername();
    public bool IsAuthenticated => !string.Equals(Username, "anonymous", StringComparison.OrdinalIgnoreCase);

    private static string GetCurrentUsername()
    {
        // 1. Check Orleans request context set by the Gateway
        var ambientUser = RequestContext.Get("DigitalBrain.ActiveUser") as string;
        if (!string.IsNullOrEmpty(ambientUser))
        {
            return ambientUser;
        }

        // 2. Check Active Scope fallback
        var activeScope = RequestContext.Get("DigitalBrain.ActiveScope") as string;
        if (!string.IsNullOrEmpty(activeScope) && !string.Equals(activeScope, "global", StringComparison.OrdinalIgnoreCase))
        {
            var parts = activeScope.Split('/');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                return parts[0];
            }
        }

        return "anonymous";
    }
}
