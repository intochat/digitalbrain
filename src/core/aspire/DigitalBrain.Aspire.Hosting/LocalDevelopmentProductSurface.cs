namespace DigitalBrain.Aspire.Hosting;

public static class LocalDevelopmentProductSurface
{
    public const int UiHttpPort = 5080;

    // Absolute URI string for Aspire parameter defaults and provider console registration.
    public const string LocalDevelopmentOAuthCallbackUri =
        "http://localhost:5080/oauth/callback";
}
