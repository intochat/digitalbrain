using System.Reflection;

namespace DigitalBrain.Access;

/// <summary>
/// Resolves the <see cref="OperationAccess"/> attribute declared on a method or contract interface.
/// </summary>
internal static class OperationAccessOf
{
    public static OperationAccess Resolve(Type contract, MethodInfo method, MethodInfo? implementation = null)
    {
        var attr = method.GetCustomAttribute<AccessAttribute>(inherit: false)
            ?? implementation?.GetCustomAttribute<AccessAttribute>(inherit: false)
            ?? contract.GetCustomAttribute<AccessAttribute>(inherit: false);

        return attr switch
        {
            PublicAttribute => OperationAccess.Public,
            SharedAttribute => OperationAccess.Shared,
            InternalAttribute => OperationAccess.Internal,
            PerAccountAttribute => OperationAccess.Internal,
            _ => contract.IsVisible ? OperationAccess.Owner : OperationAccess.Internal,
        };
    }
}
