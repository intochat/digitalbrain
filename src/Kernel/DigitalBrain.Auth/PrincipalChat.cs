using DigitalBrain.Abstractions;

namespace DigitalBrain.Auth;

public static class PrincipalChat
{
    public static string InstanceName(PrincipalId principal, string conversationName)
        => PrincipalScoped.InstanceName(principal, conversationName);
}

