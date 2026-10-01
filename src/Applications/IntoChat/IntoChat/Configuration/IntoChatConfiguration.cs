using DigitalBrain.Sdk.Identity;

namespace IntoChat;

public static class IntoChatConfiguration
{
    public const string ProtectionContainerName = "intochat-protection-v1";

    internal static IServiceCollection AddIntoChatOptions(this IServiceCollection services)
    {
        services.Configure<IdentityHostOptions>(options =>
        {
            options.CookieName = "intochat.session";
            options.ProtectionApplicationName = "IntoChat.v1";
            options.ProtectionContainerName = ProtectionContainerName;
        });
        return services;
    }
}
