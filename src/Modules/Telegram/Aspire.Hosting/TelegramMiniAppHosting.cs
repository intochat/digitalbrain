using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Telegram.Aspire.Hosting;

internal static class TelegramMiniAppHosting
{
    public static void Configure<TResource>(IResourceBuilder<TResource> kernel, IDistributedApplicationBuilder builder, string? directory)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        if (!builder.ExecutionContext.IsRunMode) { return; }
        var package = Path.GetFullPath(directory ?? Path.Combine(builder.AppHostDirectory, "../../Modules/UI/Flutter/telegram"));
        var bundle = Path.Combine(package, "build", "web");
        kernel.WithEnvironment("DigitalBrain__Telegram__MiniAppRoot", bundle);
        if (File.Exists(Path.Combine(bundle, "index.html")) && File.Exists(Path.Combine(bundle, "main.dart.js"))) { return; }
        var flutter = builder.AddExecutable("telegram-miniapp-build",
            TelegramExecutables.Resolve("flutter", builder.Configuration["DigitalBrain:FlutterCommand"]), package,
            "build", "web", "--release", "--base-href", "/telegram/app/");
        kernel.WithAnnotation(new WaitAnnotation(flutter.Resource, WaitType.WaitForCompletion, exitCode: 0));
    }
}
