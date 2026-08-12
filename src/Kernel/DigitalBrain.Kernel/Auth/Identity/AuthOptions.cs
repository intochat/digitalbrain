namespace DigitalBrain.Kernel;

internal static class AuthOptions
{
    public const string AllowLoopbackDevKey = "DigitalBrain:Auth:AllowLoopbackDev";
    public const string UsersTableName = "identityusers";
    public const string CookieName = "DigitalBrain.Auth";
    public const string PrincipalIdClaimType = "db.principal-id";
    public const string BootstrapOwnerClaimType = "db.bootstrap-owner";

    public static bool ResolveAllowLoopbackDev(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            return false;
        }

        var configured = configuration[AllowLoopbackDevKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }

        return bool.TryParse(configured, out var enabled) && enabled;
    }
}
