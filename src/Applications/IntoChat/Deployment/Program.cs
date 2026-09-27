using DigitalBrain.Deployment;
using Pulumi;

return await Deployment.RunAsync(async () =>
{
    var settings = new Config("digitalbrain");
    return await DigitalBrainDeployment.RunAsync(new DigitalBrainDeploymentOptions
    {
        Manifest = AspireManifest.Load(settings.Get("manifest") ?? "aspire-manifest.json"),
        Parameters = settings,
        SubscriptionId = settings.Get("subscriptionId") ?? Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")
            ?? throw new InvalidOperationException("Set digitalbrain:subscriptionId or AZURE_SUBSCRIPTION_ID."),
        RuntimeImage = settings.Require("runtimeImage"),
        Location = settings.Get("location") ?? "westeurope",
        NamingPrefix = settings.Get("namingPrefix") ?? "intochat",
        Name = "intochat",
    });
});
