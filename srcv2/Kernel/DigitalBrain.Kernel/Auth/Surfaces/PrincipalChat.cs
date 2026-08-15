using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal static class PrincipalChat
{
    public static string InstanceName(PrincipalId principal, string conversationName)
        => PrincipalScoped.InstanceName(principal, conversationName);
}

