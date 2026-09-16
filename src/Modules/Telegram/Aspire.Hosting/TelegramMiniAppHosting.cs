using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Telegram.Aspire.Hosting;

internal static class TelegramMiniAppHosting
{
    public static void Configure<TResource>(IResourceBuilder<TResource> kernel, IDistributedApplicationBuilder builder, string? directory, IResource parent)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        if (!builder.ExecutionContext.IsRunMode) { return; }
        var package = Path.GetFullPath(directory ?? Path.Combine(builder.AppHostDirectory, "../../Modules/UI/Flutter/shell"));
        var bundle = Path.Combine(package, "build", "telegram");
        kernel.WithEnvironment("DigitalBrain__Telegram__MiniAppRoot", bundle);
        var flutter = builder.AddExecutable("telegram-miniapp-build",
            TelegramExecutables.Resolve("flutter", builder.Configuration["DigitalBrain:FlutterCommand"]), package,
            "build", "web", "--release", "--target", "lib/main_telegram.dart", "--output", "build/telegram", "--base-href", "/telegram/app/")
            .WithParentRelationship(parent);
        kernel.WithAnnotation(new WaitAnnotation(flutter.Resource, WaitType.WaitForCompletion, exitCode: 0));
    }
}
