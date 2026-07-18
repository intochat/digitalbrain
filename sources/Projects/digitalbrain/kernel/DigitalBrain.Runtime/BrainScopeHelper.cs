using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Runtime;

public static class BrainScopeHelper
{
    public const string ActiveScopeKey = "DigitalBrain.ActiveScope";
    public const string GlobalScope = "global";

    public static string GetActiveScope()
    {
        var scope = RequestContext.Get(ActiveScopeKey) as string;
        return string.IsNullOrEmpty(scope) ? GlobalScope : scope;
    }

    public static Guid ResolveScopeGuid(string scope)
    {
        if (string.IsNullOrEmpty(scope) || scope.Equals(GlobalScope, StringComparison.OrdinalIgnoreCase))
        {
            return Guid.Empty;
        }

        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(scope.ToLowerInvariant()));
        return new Guid(hash);
    }

    public static Guid GetActiveScopeGuid()
    {
        return ResolveScopeGuid(GetActiveScope());
    }

    public static string GetScopedNeuronKey(string scope, string fqn)
    {
        if (string.IsNullOrEmpty(scope) || scope.Equals(GlobalScope, StringComparison.OrdinalIgnoreCase))
        {
            return fqn;
        }
        return $"{scope}/{fqn}";
    }

    public static string GetActiveScopedNeuronKey(string fqn)
    {
        return GetScopedNeuronKey(GetActiveScope(), fqn);
    }

    public static (string Scope, string Fqn) ParseScopedNeuronKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return (GlobalScope, string.Empty);
        }

        int slashIndex = key.LastIndexOf('/');
        if (slashIndex == -1)
        {
            return (GlobalScope, key);
        }

        return (key.Substring(0, slashIndex), key.Substring(slashIndex + 1));
    }
}
