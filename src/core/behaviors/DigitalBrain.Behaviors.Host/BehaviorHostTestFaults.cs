namespace DigitalBrain.Behaviors.Host;

internal static class BehaviorHostTestFaults
{
    private static int _refuseNextDeploy;
    private static string _reason = "unsigned-artifact";

    public static void RefuseNextDeploy(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _reason = reason;
        Volatile.Write(ref _refuseNextDeploy, 1);
    }

    public static void Reset()
    {
        Volatile.Write(ref _refuseNextDeploy, 0);
        _reason = "unsigned-artifact";
    }

    internal static void ThrowIfArmed()
    {
        if (Interlocked.Exchange(ref _refuseNextDeploy, 0) == 0)
        {
            return;
        }

        throw new BehaviorHostException(_reason);
    }
}
