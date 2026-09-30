namespace DigitalBrain.Contracts;

// A neuron contract for trusted platform code only. Scripts never see it in their catalog and the
// script edge refuses to invoke it, because a script could otherwise forge the caller context it takes.
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class PlatformOnlyAttribute : Attribute
{
    public static bool AppliesTo(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return PlatformAssemblyAttribute.IsPlatform(contract.Assembly)
            || contract.IsDefined(typeof(PlatformOnlyAttribute), inherit: false)
            || contract.GetInterfaces().Any(inherited => inherited.IsDefined(typeof(PlatformOnlyAttribute), inherit: false));
    }
}
